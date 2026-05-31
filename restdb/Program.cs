using RestDb.Data;
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
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSingleton<IDatabase>(_ => new SQLiteDatabase(GetApiConnectionString(builder.Configuration)));

            WebApplication app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseApiKeyAuthorization();

            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.MapGet("/tables", (IDatabase database) =>
            {
                return Results.Ok(database.ListTables());
            });

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

            app.MapGet("/tables/{tableName}/schema", (string tableName, IDatabase database) =>
            {
                try
                {
                    TableSchema? schema = database.GetTableSchema(tableName);
                    return schema is null
                        ? Results.NotFound(TableNotFound(tableName))
                        : Results.Ok(schema);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Invalid table name.", ex.Message));
                }
            });

            app.MapPost("/tables/{tableName}/columns", (string tableName, ColumnDefinition request, IDatabase database) =>
            {
                try
                {
                    bool migrated = database.AddColumn(tableName, $"{request.Name}:{request.Type}");
                    return migrated
                        ? Results.Ok(database.GetTableSchema(tableName))
                        : Results.NotFound(TableNotFound(tableName));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Invalid column definition.", ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Unable to add column.", ex.Message));
                }
            });

            app.MapGet("/tables/{tableName}/records", (string tableName, int? page, int? pageSize, string? filterColumn, string? filterValue, IDatabase database) =>
            {
                try
                {
                    if (!database.TableExists(tableName))
                    {
                        return Results.NotFound(TableNotFound(tableName));
                    }

                    RecordReadOptions options = new RecordReadOptions(
                        Page: page ?? 1,
                        PageSize: pageSize ?? 50,
                        FilterColumn: filterColumn,
                        FilterValue: filterValue);
                    RecordReadResult result = database.ReadRecords(tableName, options);

                    return Results.Ok(new PagedRecordsResponse(
                        result.Records,
                        result.Page,
                        result.PageSize,
                        result.TotalCount,
                        (int)Math.Ceiling(result.TotalCount / (double)result.PageSize)));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Invalid record query.", ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Unable to read records.", ex.Message));
                }
            });

            app.MapGet("/tables/{tableName}/records/{id:int}", (string tableName, int id, IDatabase database) =>
            {
                try
                {
                    if (!database.TableExists(tableName))
                    {
                        return Results.NotFound(TableNotFound(tableName));
                    }

                    Dictionary<string, object?>? record = database.ReadRecord(tableName, id);
                    return record is null
                        ? Results.NotFound(new ErrorResponse($"Record with ID {id} was not found."))
                        : Results.Ok(record);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Invalid record query.", ex.Message));
                }
                catch (SQLiteException ex)
                {
                    return Results.BadRequest(new ErrorResponse("Unable to read record.", ex.Message));
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
                    break;

                case "tables":
                    foreach (TableSummary table in database.ListTables())
                    {
                        Console.WriteLine(table.Name);
                    }
                    break;

                case "schema":
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
                    Console.WriteLine("Specify -operation create, insert, read, update, delete, tables, schema, or add-column.");
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
                "-column",
                "-connection",
                "-database",
                "-page",
                "-pagesize",
                "-filtercolumn",
                "-filtervalue"
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

            return new RecordReadOptions(page, pageSize, filterColumn, filterValue);
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

        private static ErrorResponse TableNotFound(string tableName)
        {
            return new ErrorResponse($"Table '{tableName}' was not found.", "Create the table first or verify the table name.");
        }
    }

    public record CreateTableRequest(string Name, List<ColumnDefinition> Columns);

    public record ColumnDefinition(string Name, string Type);

    public record TableResponse(string Name, List<string> Columns);

    public record PagedRecordsResponse(IReadOnlyList<Dictionary<string, object?>> Records, int Page, int PageSize, int TotalCount, int TotalPages);

    public record ErrorResponse(string Error, string? Detail = null);

    public static class ApiKeyAuthorizationExtensions
    {
        private const string ApiKeyHeaderName = "X-API-Key";

        public static IApplicationBuilder UseApiKeyAuthorization(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                string? configuredApiKey = context.RequestServices
                    .GetRequiredService<IConfiguration>()["RestDb:ApiKey"];

                if (string.IsNullOrWhiteSpace(configuredApiKey) || IsPublicRequest(context))
                {
                    await next();
                    return;
                }

                if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out Microsoft.Extensions.Primitives.StringValues providedApiKey)
                    && providedApiKey == configuredApiKey)
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse("A valid API key is required."));
            });
        }

        private static bool IsPublicRequest(HttpContext context)
        {
            PathString path = context.Request.Path;
            return path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");
        }
    }
}