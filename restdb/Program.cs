using RestDb.Classes;
using RestDb.Interfaces;
using System.Data.SQLite;
using System.Text.Json;

namespace RestDb
{
    public class Program
    {
        private const string DefaultConnectionString = "Data Source=1database.db;Version=3;";

        public static async Task Main(string[] args)
        {
            Dictionary<string, string> switches = ParseSwitches(args);

            if (switches.ContainsKey("-operation"))
            {
                RunCli(switches);
                return;
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<IDatabase>(_ => new SQLiteDatabase(GetApiConnectionString(builder.Configuration)));

            WebApplication app = builder.Build();

            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.MapPost("/tables", (CreateTableRequest request, IDatabase database) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest(new ErrorResponse("Table name is required."));
                }

                try
                {
                    List<string> columns = request.Columns.Select(column => $"{column.Name}:{column.Type}").ToList();
                    bool created = database.CreateTable(request.Name, columns);

                    return created
                        ? Results.Created($"/tables/{request.Name}", new TableResponse(request.Name, columns))
                        : Results.Conflict(new ErrorResponse($"Table '{request.Name}' already exists."));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
            });

            app.MapGet("/tables/{tableName}/records", (string tableName, IDatabase database) =>
            {
                try
                {
                    return Results.Ok(database.ReadRecords(tableName));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException)
                {
                    return Results.NotFound(new ErrorResponse($"Table '{tableName}' was not found."));
                }
            });

            app.MapGet("/tables/{tableName}/records/{id:int}", (string tableName, int id, IDatabase database) =>
            {
                try
                {
                    Dictionary<string, object?>? record = database.ReadRecord(tableName, id);
                    return record is null
                        ? Results.NotFound(new ErrorResponse($"Record with ID {id} was not found."))
                        : Results.Ok(record);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException)
                {
                    return Results.NotFound(new ErrorResponse($"Table '{tableName}' was not found."));
                }
            });

            app.MapPost("/tables/{tableName}/records", (string tableName, Dictionary<string, JsonElement> request, IDatabase database) =>
            {
                try
                {
                    Dictionary<string, object?> record = ConvertRecord(request);
                    long id = database.CreateRecord(tableName, record);
                    record["id"] = id;

                    return Results.Created($"/tables/{tableName}/records/{id}", record);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
            });

            app.MapPut("/tables/{tableName}/records/{id:int}", (string tableName, int id, Dictionary<string, JsonElement> request, IDatabase database) =>
            {
                try
                {
                    Dictionary<string, object?> record = ConvertRecord(request);
                    record["id"] = id;

                    bool updated = database.UpdateRecord(tableName, record);
                    return updated
                        ? Results.Ok(database.ReadRecord(tableName, id))
                        : Results.NotFound(new ErrorResponse($"Record with ID {id} was not found."));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
            });

            app.MapPatch("/tables/{tableName}/records/{id:int}", (string tableName, int id, Dictionary<string, JsonElement> request, IDatabase database) =>
            {
                try
                {
                    Dictionary<string, object?> record = ConvertRecord(request);
                    record["id"] = id;

                    bool updated = database.UpdateRecord(tableName, record);
                    return updated
                        ? Results.Ok(database.ReadRecord(tableName, id))
                        : Results.NotFound(new ErrorResponse($"Record with ID {id} was not found."));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
            });

            app.MapDelete("/tables/{tableName}/records/{id:int}", (string tableName, int id, IDatabase database) =>
            {
                try
                {
                    bool deleted = database.DeleteRecord(tableName, id);
                    return deleted
                        ? Results.NoContent()
                        : Results.NotFound(new ErrorResponse($"Record with ID {id} was not found."));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse(ex.Message));
                }
            });

            await app.RunAsync();
        }

        private static void RunCli(Dictionary<string, string> switches)
        {
            SQLiteDatabase database = new SQLiteDatabase(GetCliConnectionString(switches));

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
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
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
                    }
                    else
                    {
                        List<Dictionary<string, object?>> records = database.ReadRecords(switches["-table"]);
                        if (records != null && records.Count > 0)
                        {
                            Console.WriteLine($"Table: {switches["-table"]}");
                            foreach (Dictionary<string, object?> record in records)
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
                    break;

                case "update":
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
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
                    break;

                case "delete":
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
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
                    break;

                default:
                    Console.WriteLine("Specify -operation create, insert, read, update, or delete.");
                    break;
            }
        }

        private static Dictionary<string, string> ParseSwitches(string[] args)
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

        private static Dictionary<string, object?> GetRecordFields(Dictionary<string, string> switches)
        {
            HashSet<string> reservedSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "-operation",
                "-table",
                "-id",
                "-columns",
                "-connection",
                "-database"
            };

            return switches
                .Where(switchValue => !reservedSwitches.Contains(switchValue.Key))
                .ToDictionary(
                    switchValue => switchValue.Key.TrimStart('-'),
                    switchValue => (object?)switchValue.Value,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string GetApiConnectionString(IConfiguration configuration)
        {
            return configuration.GetConnectionString("RestDb")
                ?? configuration["RestDb:ConnectionString"]
                ?? DefaultConnectionString;
        }

        private static string GetCliConnectionString(Dictionary<string, string> switches)
        {
            if (switches.TryGetValue("-connection", out string? connectionString))
            {
                return connectionString;
            }

            return switches.TryGetValue("-database", out string? databasePath)
                ? $"Data Source={databasePath};Version=3;"
                : DefaultConnectionString;
        }

        private static Dictionary<string, object?> ConvertRecord(Dictionary<string, JsonElement> request)
        {
            return request.ToDictionary(field => field.Key, field => ConvertJsonElement(field.Value), StringComparer.OrdinalIgnoreCase);
        }

        private static object? ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out long longValue) => longValue,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }
    }

    public record CreateTableRequest(string Name, List<ColumnDefinition> Columns);

    public record ColumnDefinition(string Name, string Type);

    public record TableResponse(string Name, List<string> Columns);

    public record ErrorResponse(string Error);
}