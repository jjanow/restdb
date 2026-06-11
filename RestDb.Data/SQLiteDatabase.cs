using System.Data.SQLite;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace RestDb.Data;

public class SQLiteDatabase : IDatabase
{
    public const string MeterName = "RestDb.Data";

    private static readonly Meter Meter = new Meter(MeterName, "1.0");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "restdb_database_operation_duration_ms",
        unit: "ms",
        description: "Duration of SQLiteDatabase operations in milliseconds");

    private static readonly Regex IdentifierPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedColumnTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT",
        "INTEGER",
        "REAL",
        "BLOB",
        "NUMERIC"
    };
    private static readonly HashSet<string> AllowedFilterOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eq",
        "ne",
        "contains",
        "startsWith",
        "endsWith",
        "gt",
        "gte",
        "lt",
        "lte",
        "isNull",
        "isNotNull"
    };
    private static readonly HashSet<string> AllowedSortDirections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ASC",
        "DESC"
    };
    private static readonly HashSet<string> RowidAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "rowid", "oid", "_rowid_"
    };
    private const string IdColumnName = "id";

    private readonly string connectionString;

    public SQLiteDatabase(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public long CreateRecord(string tableName, Dictionary<string, object?> record)
    {
        return Measure("CreateRecord", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (record.Count == 0)
            {
                throw new ArgumentException("At least one record field is required.", nameof(record));
            }

            List<string> columns = new List<string>();
            List<string> values = new List<string>();
            List<SQLiteParameter> parameters = new List<SQLiteParameter>();
            int parameterIndex = 0;

            foreach (KeyValuePair<string, object?> field in record)
            {
                if (field.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ValidateIdentifier(field.Key, nameof(record));
                string parameterName = $"@p{parameterIndex++}";
                columns.Add(QuoteIdentifier(field.Key));
                values.Add(parameterName);
                parameters.Add(new SQLiteParameter(parameterName, field.Value ?? DBNull.Value));
            }

            if (columns.Count == 0)
            {
                throw new ArgumentException("At least one non-id record field is required.", nameof(record));
            }

            string query = $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}); SELECT last_insert_rowid();";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.Parameters.AddRange(parameters.ToArray());
            return (long)command.ExecuteScalar();
        });
    }

    public RecordReadResult ReadRecords(string tableName, RecordReadOptions? options = null)
    {
        return Measure("ReadRecords", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            RecordReadOptions effectiveOptions = NormalizeReadOptions(options);

            using SQLiteConnection connection = CreateConnection();
            connection.Open();

            ValidateReadColumns(connection, tableName, effectiveOptions);
            List<SQLiteParameter> parameters = new List<SQLiteParameter>();
            string whereClause = BuildFilterClause(effectiveOptions, parameters);

            int totalCount = ReadRecordCount(connection, tableName, whereClause, parameters);
            List<Dictionary<string, object?>> records = ReadRecordPage(connection, tableName, whereClause, parameters, effectiveOptions);

            return new RecordReadResult(records, effectiveOptions.Page, effectiveOptions.PageSize, totalCount);
        });
    }

    public Dictionary<string, object?>? ReadRecord(string tableName, int id)
    {
        return Measure("ReadRecord", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE id = @id";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.Parameters.AddWithValue("@id", id);

            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadCurrentRecord(reader) : null;
        });
    }

    public bool UpdateRecord(string tableName, Dictionary<string, object?> record)
    {
        return Measure("UpdateRecord", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (!record.TryGetValue("id", out object? idValue) || idValue is null)
            {
                throw new ArgumentException("The record must include an id value.", nameof(record));
            }

            List<string> assignments = new List<string>();
            List<SQLiteParameter> parameters = new List<SQLiteParameter>
            {
                new SQLiteParameter("@id", idValue)
            };
            int parameterIndex = 0;

            foreach (KeyValuePair<string, object?> field in record.Where(field => !field.Key.Equals("id", StringComparison.OrdinalIgnoreCase)))
            {
                ValidateIdentifier(field.Key, nameof(record));
                string parameterName = $"@p{parameterIndex++}";
                assignments.Add($"{QuoteIdentifier(field.Key)} = {parameterName}");
                parameters.Add(new SQLiteParameter(parameterName, field.Value ?? DBNull.Value));
            }

            if (assignments.Count == 0)
            {
                throw new ArgumentException("At least one field is required to update a record.", nameof(record));
            }

            string query = $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", assignments)} WHERE id = @id";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.Parameters.AddRange(parameters.ToArray());
            return command.ExecuteNonQuery() > 0;
        });
    }

    public bool DeleteRecord(string tableName, int id)
    {
        return Measure("DeleteRecord", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE id = @id";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.Parameters.AddWithValue("@id", id);
            return command.ExecuteNonQuery() > 0;
        });
    }

    public bool CreateTable(string tableName, List<string> columns)
    {
        return Measure("CreateTable", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (TableExists(tableName))
            {
                return false;
            }

            List<string> definitions = new List<string> { "id INTEGER PRIMARY KEY AUTOINCREMENT" };
            HashSet<string> columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string column in columns)
            {
                ColumnDefinitionParts parsedColumn = ParseColumnDefinition(column);
                if (!columnNames.Add(parsedColumn.Name))
                {
                    throw new ArgumentException($"Duplicate column name: {parsedColumn.Name}", nameof(columns));
                }

                definitions.Add(parsedColumn.Sql);
            }

            string query = $"CREATE TABLE {QuoteIdentifier(tableName)} ({string.Join(", ", definitions)})";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.ExecuteNonQuery();
            return true;
        });
    }

    public bool AddColumn(string tableName, string column)
    {
        return Measure("AddColumn", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (!TableExists(tableName))
            {
                return false;
            }

            string query = $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {ParseColumnDefinition(column).Sql}";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.ExecuteNonQuery();
            return true;
        });
    }

    public bool RenameColumn(string tableName, string columnName, string newColumnName)
    {
        return Measure("RenameColumn", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));
            ValidateIdentifier(columnName, nameof(columnName));
            ValidateIdentifier(newColumnName, nameof(newColumnName));
            ValidateMutableColumn(columnName, nameof(columnName));
            ValidateNewColumnName(newColumnName, nameof(newColumnName));

            if (!TableExists(tableName))
            {
                return false;
            }

            string query = $"ALTER TABLE {QuoteIdentifier(tableName)} RENAME COLUMN {QuoteIdentifier(columnName)} TO {QuoteIdentifier(newColumnName)}";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.ExecuteNonQuery();
            return true;
        });
    }

    public bool DropColumn(string tableName, string columnName)
    {
        return Measure("DropColumn", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));
            ValidateIdentifier(columnName, nameof(columnName));
            ValidateMutableColumn(columnName, nameof(columnName));

            if (!TableExists(tableName))
            {
                return false;
            }

            string query = $"ALTER TABLE {QuoteIdentifier(tableName)} DROP COLUMN {QuoteIdentifier(columnName)}";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.ExecuteNonQuery();
            return true;
        });
    }

    public bool TableExists(string tableName)
    {
        return Measure("TableExists", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @tableName";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            command.Parameters.AddWithValue("@tableName", tableName);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.HasRows;
        });
    }

    public IReadOnlyList<TableSummary> ListTables()
    {
        return Measure("ListTables", () =>
        {
            const string query = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using SQLiteConnection connection = CreateConnection();
            using SQLiteCommand command = new SQLiteCommand(query, connection);

            connection.Open();
            using SQLiteDataReader reader = command.ExecuteReader();

            List<TableSummary> tables = new List<TableSummary>();
            while (reader.Read())
            {
                tables.Add(new TableSummary(reader.GetString(0)));
            }

            return tables;
        });
    }

    public TableSchema? GetTableSchema(string tableName)
    {
        return Measure("GetTableSchema", () =>
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (!TableExists(tableName))
            {
                return null;
            }

            using SQLiteConnection connection = CreateConnection();
            connection.Open();
            return new TableSchema(tableName, ReadSchemaFromConnection(connection, tableName));
        });
    }

    private SQLiteConnection CreateConnection()
    {
        return new SQLiteConnection(connectionString, true);
    }

    private static T Measure<T>(string operation, Func<T> work)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return work();
        }
        finally
        {
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            OperationDuration.Record(elapsedMs, new KeyValuePair<string, object?>("operation", operation));
        }
    }

    private static void Measure(string operation, Action work)
    {
        Measure<int>(operation, () => { work(); return 0; });
    }

    private static RecordReadOptions NormalizeReadOptions(RecordReadOptions? options)
    {
        int page = Math.Max(options?.Page ?? 1, 1);
        int pageSize = Math.Clamp(options?.PageSize ?? 50, 1, 500);

        return new RecordReadOptions(
            page,
            pageSize,
            string.IsNullOrWhiteSpace(options?.FilterColumn) ? null : options.FilterColumn,
            string.IsNullOrWhiteSpace(options?.FilterValue) ? null : options.FilterValue,
            string.IsNullOrWhiteSpace(options?.FilterOperator) ? null : NormalizeFilterOperator(options.FilterOperator),
            string.IsNullOrWhiteSpace(options?.SortColumn) ? null : options.SortColumn,
            string.IsNullOrWhiteSpace(options?.SortDirection) ? null : NormalizeSortDirection(options.SortDirection));
    }

    private static ColumnDefinitionParts ParseColumnDefinition(string column)
    {
        string[] parts = column.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid column format: {column}");
        }

        ValidateIdentifier(parts[0], nameof(column));
        ValidateNewColumnName(parts[0], nameof(column));
        string columnType = parts[1].ToUpperInvariant();
        if (!AllowedColumnTypes.Contains(columnType))
        {
            throw new ArgumentException($"Invalid column type: {parts[1]}");
        }

        return new ColumnDefinitionParts(parts[0], $"{QuoteIdentifier(parts[0])} {columnType}");
    }

    private static string BuildFilterClause(RecordReadOptions options, List<SQLiteParameter> parameters)
    {
        if (options.FilterColumn is null && options.FilterValue is null && options.FilterOperator is null)
        {
            return string.Empty;
        }

        if (options.FilterColumn is null)
        {
            throw new ArgumentException("filterColumn is required when filtering records.", nameof(options));
        }

        string filterOperator = options.FilterOperator ?? "eq";

        if (!AllowedFilterOperators.Contains(filterOperator))
        {
            throw new ArgumentException($"Unsupported filterOperator: {filterOperator}", nameof(options));
        }

        return filterOperator switch
        {
            "eq" => BuildValueFilter(options.FilterColumn, "=", options.FilterValue, parameters),
            "ne" => BuildValueFilter(options.FilterColumn, "<>", options.FilterValue, parameters),
            "contains" => BuildLikeFilter(options.FilterColumn, $"%{EscapeLikeValue(RequireFilterValue(options.FilterValue))}%", parameters),
            "startsWith" => BuildLikeFilter(options.FilterColumn, $"{EscapeLikeValue(RequireFilterValue(options.FilterValue))}%", parameters),
            "endsWith" => BuildLikeFilter(options.FilterColumn, $"%{EscapeLikeValue(RequireFilterValue(options.FilterValue))}", parameters),
            "gt" => BuildValueFilter(options.FilterColumn, ">", options.FilterValue, parameters),
            "gte" => BuildValueFilter(options.FilterColumn, ">=", options.FilterValue, parameters),
            "lt" => BuildValueFilter(options.FilterColumn, "<", options.FilterValue, parameters),
            "lte" => BuildValueFilter(options.FilterColumn, "<=", options.FilterValue, parameters),
            "isNull" => $" WHERE {QuoteIdentifier(options.FilterColumn)} IS NULL",
            "isNotNull" => $" WHERE {QuoteIdentifier(options.FilterColumn)} IS NOT NULL",
            _ => throw new ArgumentException($"Unsupported filterOperator: {filterOperator}", nameof(options))
        };
    }

    private static int ReadRecordCount(SQLiteConnection connection, string tableName, string whereClause, IReadOnlyCollection<SQLiteParameter> parameters)
    {
        string query = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}{whereClause}";
        using SQLiteCommand command = new SQLiteCommand(query, connection);
        command.Parameters.AddRange(CloneParameters(parameters).ToArray());
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<Dictionary<string, object?>> ReadRecordPage(
        SQLiteConnection connection,
        string tableName,
        string whereClause,
        IReadOnlyCollection<SQLiteParameter> filterParameters,
        RecordReadOptions options)
    {
        int offset = (options.Page - 1) * options.PageSize;
        string query = $"SELECT * FROM {QuoteIdentifier(tableName)}{whereClause}{BuildOrderByClause(options)} LIMIT @limit OFFSET @offset";
        using SQLiteCommand command = new SQLiteCommand(query, connection);

        command.Parameters.AddRange(CloneParameters(filterParameters).ToArray());
        command.Parameters.AddWithValue("@limit", options.PageSize);
        command.Parameters.AddWithValue("@offset", offset);

        using SQLiteDataReader reader = command.ExecuteReader();
        List<Dictionary<string, object?>> records = new List<Dictionary<string, object?>>();

        while (reader.Read())
        {
            records.Add(ReadCurrentRecord(reader));
        }

        return records;
    }

    private static List<SQLiteParameter> CloneParameters(IEnumerable<SQLiteParameter> parameters)
    {
        return parameters
            .Select(parameter => new SQLiteParameter(parameter.ParameterName, parameter.Value))
            .ToList();
    }

    private static string BuildValueFilter(string columnName, string sqlOperator, string? filterValue, List<SQLiteParameter> parameters)
    {
        parameters.Add(new SQLiteParameter("@filterValue", RequireFilterValue(filterValue)));
        return $" WHERE {QuoteIdentifier(columnName)} {sqlOperator} @filterValue";
    }

    private static string BuildLikeFilter(string columnName, string filterValue, List<SQLiteParameter> parameters)
    {
        parameters.Add(new SQLiteParameter("@filterValue", filterValue));
        return $" WHERE {QuoteIdentifier(columnName)} LIKE @filterValue ESCAPE '\\'";
    }

    private static string BuildOrderByClause(RecordReadOptions options)
    {
        string sortColumn = options.SortColumn ?? "id";
        string sortDirection = options.SortDirection ?? "ASC";

        if (!AllowedSortDirections.Contains(sortDirection))
        {
            throw new ArgumentException($"Unsupported sortDirection: {sortDirection}", nameof(options));
        }

        return $" ORDER BY {QuoteIdentifier(sortColumn)} {sortDirection}";
    }

    private static void ValidateReadColumns(SQLiteConnection connection, string tableName, RecordReadOptions options)
    {
        if (options.FilterColumn is null && options.SortColumn is null)
        {
            return;
        }

        List<ColumnSchema> schema = ReadSchemaFromConnection(connection, tableName);

        if (schema.Count == 0)
        {
            throw new TableNotFoundException(tableName);
        }

        HashSet<string> columnNames = new HashSet<string>(schema.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        ValidateRequestedColumn(columnNames, options.FilterColumn, nameof(options.FilterColumn));
        ValidateRequestedColumn(columnNames, options.SortColumn, nameof(options.SortColumn));
    }

    private static List<ColumnSchema> ReadSchemaFromConnection(SQLiteConnection connection, string tableName)
    {
        using SQLiteCommand command = new SQLiteCommand($"PRAGMA table_info({QuoteIdentifier(tableName)})", connection);
        using SQLiteDataReader reader = command.ExecuteReader();
        List<ColumnSchema> columns = new List<ColumnSchema>();

        while (reader.Read())
        {
            columns.Add(new ColumnSchema(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetValue(4).ToString(),
                reader.GetInt32(5) == 1));
        }

        return columns;
    }

    private static void ValidateRequestedColumn(HashSet<string> columns, string? columnName, string parameterName)
    {
        if (columnName is null)
        {
            return;
        }

        ValidateIdentifier(columnName, parameterName);
        if (!RowidAliases.Contains(columnName) && !columns.Contains(columnName))
        {
            throw new ArgumentException($"Column '{columnName}' does not exist on the requested table.", parameterName);
        }
    }

    private static string NormalizeFilterOperator(string filterOperator)
    {
        return filterOperator.ToLowerInvariant() switch
        {
            "=" or "equals" => "eq",
            "!=" or "<>" or "notequals" => "ne",
            "eq" => "eq",
            "ne" => "ne",
            "contains" => "contains",
            "startswith" => "startsWith",
            "endswith" => "endsWith",
            ">" => "gt",
            ">=" => "gte",
            "<" => "lt",
            "<=" => "lte",
            "gt" => "gt",
            "gte" => "gte",
            "lt" => "lt",
            "lte" => "lte",
            "isnull" => "isNull",
            "isnotnull" => "isNotNull",
            _ => filterOperator
        };
    }

    private static string NormalizeSortDirection(string sortDirection)
    {
        return sortDirection.ToLowerInvariant() switch
        {
            "asc" or "ascending" => "ASC",
            "desc" or "descending" => "DESC",
            _ => sortDirection
        };
    }

    private static string RequireFilterValue(string? filterValue)
    {
        if (filterValue is null)
        {
            throw new ArgumentException("filterValue is required for this filterOperator.");
        }

        return filterValue;
    }

    private static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static Dictionary<string, object?> ReadCurrentRecord(SQLiteDataReader reader)
    {
        Dictionary<string, object?> record = new Dictionary<string, object?>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return record;
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException($"Invalid identifier: {value}", parameterName);
        }
    }

    private static void ValidateMutableColumn(string columnName, string parameterName)
    {
        if (columnName.Equals(IdColumnName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The id column cannot be renamed or dropped.", parameterName);
        }
    }

    private static void ValidateNewColumnName(string columnName, string parameterName)
    {
        if (columnName.Equals(IdColumnName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The id column is reserved.", parameterName);
        }
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed record ColumnDefinitionParts(string Name, string Sql);
}
