using RestDb.Data;

namespace RestDb;

public static class CliApplication
{
    public static bool ShouldRun(Dictionary<string, string> switches)
    {
        return switches.ContainsKey("-operation");
    }

    public static void Run(Dictionary<string, string> switches)
    {
        SQLiteDatabase database = new SQLiteDatabase(GetConnectionString(switches));

        try
        {
            switch (switches.ContainsKey("-operation") ? switches["-operation"] : "")
            {
                case "create":
                    string tableName = switches["-table"];
                    List<string> columns = switches.ContainsKey("-columns") ? switches["-columns"].Split(',').ToList() : new List<string>();

                    bool created = database.CreateTable(tableName, columns);
                    Console.WriteLine(created ? $"Table '{tableName}' created." : $"Table '{tableName}' already exists.");
                    break;

                case "insert":
                    Dictionary<string, object?> recordToInsert = GetRecordFields(switches);
                    long newId = database.CreateRecord(switches["-table"], recordToInsert);
                    Console.WriteLine($"Record with ID {newId} created.");
                    break;

                case "read":
                    ReadRecords(switches, database);
                    break;

                case "tables":
                    foreach (TableSummary table in database.ListTables())
                    {
                        Console.WriteLine(table.Name);
                    }
                    break;

                case "schema":
                    PrintSchema(switches, database);
                    break;

                case "add-column":
                    if (!switches.ContainsKey("-table") || !switches.ContainsKey("-column"))
                    {
                        Console.WriteLine("Table and column are required to add a column.");
                        return;
                    }

                    bool migrated = database.AddColumn(switches["-table"], switches["-column"]);
                    Console.WriteLine(migrated ? $"Column added to table '{switches["-table"]}'." : $"Table '{switches["-table"]}' was not found.");
                    break;

                case "rename-column":
                    if (!switches.ContainsKey("-table") || !switches.ContainsKey("-column") || !switches.ContainsKey("-newname"))
                    {
                        Console.WriteLine("Table, column, and newname are required to rename a column.");
                        return;
                    }

                    bool renamed = database.RenameColumn(switches["-table"], switches["-column"], switches["-newname"]);
                    Console.WriteLine(renamed ? $"Column renamed in table '{switches["-table"]}'." : $"Table '{switches["-table"]}' was not found.");
                    break;

                case "drop-column":
                    if (!switches.ContainsKey("-table") || !switches.ContainsKey("-column"))
                    {
                        Console.WriteLine("Table and column are required to drop a column.");
                        return;
                    }

                    bool dropped = database.DropColumn(switches["-table"], switches["-column"]);
                    Console.WriteLine(dropped ? $"Column dropped from table '{switches["-table"]}'." : $"Table '{switches["-table"]}' was not found.");
                    break;

                case "update":
                    UpdateRecord(switches, database);
                    break;

                case "delete":
                    DeleteRecord(switches, database);
                    break;

                default:
                    Console.WriteLine("Specify -operation create, insert, read, update, delete, tables, schema, add-column, rename-column, or drop-column.");
                    break;
            }
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        catch (TableNotFoundException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    public static Dictionary<string, string> ParseSwitches(string[] args)
    {
        Dictionary<string, string> switches = new Dictionary<string, string>();

        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i].ToLowerInvariant();

            if (key.StartsWith("-") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                string value = args[i + 1];
                switches[key] = value;
                i++;
            }
        }

        return switches;
    }

    private static void ReadRecords(Dictionary<string, string> switches, SQLiteDatabase database)
    {
        if (switches.ContainsKey("-id"))
        {
            if (!Int32.TryParse(switches["-id"], out int id))
            {
                Console.WriteLine("Invalid ID specified.");
                return;
            }

            Dictionary<string, object?>? record = database.ReadRecord(switches["-table"], id);
            if (record != null)
            {
                Console.WriteLine($"Record ID: {id}");
                foreach (KeyValuePair<string, object?> kvp in record)
                {
                    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
            }
            else
            {
                Console.WriteLine($"Record with ID {id} not found.");
            }

            return;
        }

        RecordReadOptions options = GetRecordReadOptions(switches);
        RecordReadResult result = database.ReadRecords(switches["-table"], options);
        if (result.Records.Count > 0)
        {
            Console.WriteLine($"Table: {switches["-table"]} (page {result.Page}, page size {result.PageSize}, total {result.TotalCount})");
            foreach (Dictionary<string, object?> record in result.Records)
            {
                Console.WriteLine($"Record ID: {record["id"]}");
                foreach (KeyValuePair<string, object?> kvp in record)
                {
                    if (kvp.Key != "id")
                    {
                        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                    }
                }
                Console.WriteLine("");
            }
        }
        else
        {
            Console.WriteLine($"No records found in table {switches["-table"]}.");
        }
    }

    private static void PrintSchema(Dictionary<string, string> switches, SQLiteDatabase database)
    {
        if (!switches.ContainsKey("-table"))
        {
            Console.WriteLine("Table is required to inspect schema.");
            return;
        }

        TableSchema? schema = database.GetTableSchema(switches["-table"]);
        if (schema is null)
        {
            Console.WriteLine($"Table '{switches["-table"]}' was not found.");
            return;
        }

        Console.WriteLine($"Table: {schema.Name}");
        foreach (ColumnSchema column in schema.Columns)
        {
            string primaryKey = column.PrimaryKey ? " primary key" : string.Empty;
            string nullable = column.NotNull ? " not null" : string.Empty;
            Console.WriteLine($"{column.Name}: {column.Type}{primaryKey}{nullable}");
        }
    }

    private static void UpdateRecord(Dictionary<string, string> switches, SQLiteDatabase database)
    {
        if (switches.ContainsKey("-id"))
        {
            if (!Int32.TryParse(switches["-id"], out int id))
            {
                Console.WriteLine("Invalid ID specified.");
                return;
            }

            Dictionary<string, object?> recordToUpdate = GetRecordFields(switches);
            recordToUpdate["id"] = id;
            bool updated = database.UpdateRecord(switches["-table"], recordToUpdate);
            Console.WriteLine(updated ? $"Record with ID {id} updated." : $"Record with ID {id} not found.");
        }
        else
        {
            Console.WriteLine("ID is required to update a record.");
        }
    }

    private static void DeleteRecord(Dictionary<string, string> switches, SQLiteDatabase database)
    {
        if (switches.ContainsKey("-id"))
        {
            if (!Int32.TryParse(switches["-id"], out int id))
            {
                Console.WriteLine("Invalid ID specified.");
                return;
            }

            bool deleted = database.DeleteRecord(switches["-table"], id);
            Console.WriteLine(deleted ? $"Record with ID {id} deleted." : $"Record with ID {id} not found.");
        }
        else
        {
            Console.WriteLine("ID not specified.");
        }
    }

    private static Dictionary<string, object?> GetRecordFields(Dictionary<string, string> switches)
    {
        HashSet<string> reservedSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-operation",
            "-table",
            "-id",
            "-columns",
            "-column",
            "-connection",
            "-database",
            "-page",
            "-pagesize",
            "-filtercolumn",
            "-filtervalue",
            "-filteroperator",
            "-sortcolumn",
            "-sortdirection",
            "-newname"
        };

        return switches
            .Where(switchValue => !reservedSwitches.Contains(switchValue.Key))
            .ToDictionary(
                switchValue => switchValue.Key.TrimStart('-'),
                switchValue => (object?)switchValue.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string GetConnectionString(Dictionary<string, string> switches)
    {
        if (switches.TryGetValue("-connection", out string? connectionString))
        {
            return connectionString;
        }

        return switches.TryGetValue("-database", out string? databasePath)
            ? $"Data Source={databasePath};Version=3;"
            : Program.DefaultConnectionString;
    }

    private static RecordReadOptions GetRecordReadOptions(Dictionary<string, string> switches)
    {
        int page = 1;
        int pageSize = 50;

        if (switches.TryGetValue("-page", out string? pageValue) && !Int32.TryParse(pageValue, out page))
        {
            page = 1;
        }

        if (switches.TryGetValue("-pagesize", out string? pageSizeValue) && !Int32.TryParse(pageSizeValue, out pageSize))
        {
            pageSize = 50;
        }

        switches.TryGetValue("-filtercolumn", out string? filterColumn);
        switches.TryGetValue("-filtervalue", out string? filterValue);
        switches.TryGetValue("-filteroperator", out string? filterOperator);
        switches.TryGetValue("-sortcolumn", out string? sortColumn);
        switches.TryGetValue("-sortdirection", out string? sortDirection);

        return new RecordReadOptions(page, pageSize, filterColumn, filterValue, filterOperator, sortColumn, sortDirection);
    }
}
