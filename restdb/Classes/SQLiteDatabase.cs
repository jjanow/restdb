using System.Data.SQLite;
using System.Text.RegularExpressions;

namespace RestDb.Classes
{
    public class SQLiteDatabase : RestDb.Interfaces.IDatabase
    {
        private static readonly Regex IdentifierPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedColumnTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TEXT",
            "INTEGER",
            "REAL",
            "BLOB",
            "NUMERIC"
        };

        private readonly string connectionString;

        public SQLiteDatabase(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public long CreateRecord(string tableName, Dictionary<string, object?> record)
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
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddRange(parameters.ToArray());
                return (long)command.ExecuteScalar();
            }
        }

        public List<Dictionary<string, object?>> ReadRecords(string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = $"SELECT * FROM {QuoteIdentifier(tableName)}";
            using SQLiteConnection connection = CreateConnection();
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();

                List<Dictionary<string, object?>> records = new List<Dictionary<string, object?>>();

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Dictionary<string, object?> record = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }

                        records.Add(record);
                    }
                }

                return records;
            }
        }

        public Dictionary<string, object?>? ReadRecord(string tableName, int id)
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE id = @id";
            using SQLiteConnection connection = CreateConnection();
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@id", id);

                Dictionary<string, object?>? record = null;

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        record = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                    }
                }

                return record;
            }
        }

        public bool UpdateRecord(string tableName, Dictionary<string, object?> record)
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
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddRange(parameters.ToArray());
                return command.ExecuteNonQuery() > 0;
            }
        }

        public bool DeleteRecord(string tableName, int id)
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE id = @id";
            using SQLiteConnection connection = CreateConnection();
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@id", id);
                return command.ExecuteNonQuery() > 0;
            }
        }

        public bool CreateTable(string tableName, List<string> columns)
        {
            ValidateIdentifier(tableName, nameof(tableName));

            if (TableExists(tableName))
            {
                return false;
            }

            List<string> definitions = new List<string> { "id INTEGER PRIMARY KEY AUTOINCREMENT" };

            foreach (string column in columns)
            {
                string[] parts = column.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException($"Invalid column format: {column}");
                }

                ValidateIdentifier(parts[0], nameof(columns));
                string columnType = parts[1].ToUpperInvariant();
                if (!AllowedColumnTypes.Contains(columnType))
                {
                    throw new ArgumentException($"Invalid column type: {parts[1]}");
                }

                definitions.Add($"{QuoteIdentifier(parts[0])} {columnType}");
            }

            string query = $"CREATE TABLE {QuoteIdentifier(tableName)} ({string.Join(", ", definitions)})";
            using SQLiteConnection connection = CreateConnection();
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
                return true;
            }
        }

        public bool TableExists(string tableName)
        {
            ValidateIdentifier(tableName, nameof(tableName));

            string query = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @tableName";
            using SQLiteConnection connection = CreateConnection();
            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddWithValue("@tableName", tableName);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    return reader.HasRows;
                }
            }
        }

        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection(connectionString, true);
        }

        private static void ValidateIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern.IsMatch(value))
            {
                throw new ArgumentException($"Invalid identifier: {value}", parameterName);
            }
        }

        private static string QuoteIdentifier(string value)
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}