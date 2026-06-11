using OpenTelemetry.Metrics;
using RestDb.Data;
using System.Data.SQLite;
using System.Text.Json;

namespace RestDb;

public static class RestApiApplication
{
    public static Task RunAsync(string[] args)
    {
        WebApplication app = Build(args);
        return app.RunAsync();
    }

    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton<IDatabase>(_ => new SQLiteDatabase(GetConnectionString(builder.Configuration)));
        builder.Services.AddRestDbApiKeyAuthentication();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(SQLiteDatabase.MeterName)
                .AddPrometheusExporter());

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("RestDb:ExposeMetrics"))
        {
            app.MapPrometheusScrapingEndpoint();
        }

        MapRoutes(app);

        return app;
    }

    private static void MapRoutes(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        RouteGroupBuilder tables = app.MapGroup("/tables")
            .RequireAuthorization(RestDbAuthorizationPolicies.ApiAccess);

        tables.MapGet("", (IDatabase database) =>
        {
            return Results.Ok(database.ListTables());
        });

        tables.MapPost("", (CreateTableRequest request, IDatabase database) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ErrorResponse("Table name is required."));
            }

            if (request.Columns is null || request.Columns.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("At least one column is required."));
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
            catch (SQLiteException ex)
            {
                LogSqliteException(app.Logger, ex, "creating table");
                return Results.BadRequest(new ErrorResponse("Unable to create table."));
            }
        });

        tables.MapGet("/{tableName}/schema", (string tableName, IDatabase database) =>
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

        tables.MapPost("/{tableName}/columns", (string tableName, ColumnDefinition request, IDatabase database) =>
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
                LogSqliteException(app.Logger, ex, "adding column");
                return Results.BadRequest(new ErrorResponse("Unable to add column."));
            }
        });

        tables.MapPost("/{tableName}/columns/{columnName}/rename", (string tableName, string columnName, RenameColumnRequest request, IDatabase database) =>
        {
            try
            {
                bool migrated = database.RenameColumn(tableName, columnName, request.Name);
                return migrated
                    ? Results.Ok(database.GetTableSchema(tableName))
                    : Results.NotFound(TableNotFound(tableName));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("Invalid column rename.", ex.Message));
            }
            catch (SQLiteException ex)
            {
                LogSqliteException(app.Logger, ex, "renaming column");
                return Results.BadRequest(new ErrorResponse("Unable to rename column."));
            }
        });

        tables.MapDelete("/{tableName}/columns/{columnName}", (string tableName, string columnName, IDatabase database) =>
        {
            try
            {
                bool migrated = database.DropColumn(tableName, columnName);
                return migrated
                    ? Results.Ok(database.GetTableSchema(tableName))
                    : Results.NotFound(TableNotFound(tableName));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("Invalid column drop.", ex.Message));
            }
            catch (SQLiteException ex)
            {
                LogSqliteException(app.Logger, ex, "dropping column");
                return Results.BadRequest(new ErrorResponse("Unable to drop column."));
            }
        });

        tables.MapGet("/{tableName}/records", (string tableName, int? page, int? pageSize, string? filterColumn, string? filterValue, string? filterOperator, string? sortColumn, string? sortDirection, IDatabase database) =>
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
                    FilterValue: filterValue,
                    FilterOperator: filterOperator,
                    SortColumn: sortColumn,
                    SortDirection: sortDirection);
                RecordReadResult result = database.ReadRecords(tableName, options);

                return Results.Ok(new PagedRecordsResponse(
                    result.Records,
                    result.Page,
                    result.PageSize,
                    result.TotalCount,
                    (int)Math.Ceiling(result.TotalCount / (double)result.PageSize)));
            }
            catch (TableNotFoundException ex)
            {
                return Results.NotFound(new ErrorResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("Invalid record query.", ex.Message));
            }
            catch (SQLiteException ex)
            {
                LogSqliteException(app.Logger, ex, "reading records");
                return Results.BadRequest(new ErrorResponse("Unable to read records."));
            }
        });

        tables.MapGet("/{tableName}/records/{id:int}", (string tableName, int id, IDatabase database) =>
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
                LogSqliteException(app.Logger, ex, "reading record");
                return Results.BadRequest(new ErrorResponse("Unable to read record."));
            }
        });

        tables.MapPost("/{tableName}/records", (string tableName, Dictionary<string, JsonElement> request, IDatabase database) =>
        {
            try
            {
                if (!database.TableExists(tableName))
                {
                    return Results.NotFound(TableNotFound(tableName));
                }

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
                LogSqliteException(app.Logger, ex, "creating record");
                return Results.BadRequest(new ErrorResponse("Unable to create record."));
            }
        });

        tables.MapPut("/{tableName}/records/{id:int}", (string tableName, int id, Dictionary<string, JsonElement> request, IDatabase database) =>
        {
            try
            {
                if (!database.TableExists(tableName))
                {
                    return Results.NotFound(TableNotFound(tableName));
                }

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
                LogSqliteException(app.Logger, ex, "updating record");
                return Results.BadRequest(new ErrorResponse("Unable to update record."));
            }
        });

        tables.MapPatch("/{tableName}/records/{id:int}", (string tableName, int id, Dictionary<string, JsonElement> request, IDatabase database) =>
        {
            try
            {
                if (!database.TableExists(tableName))
                {
                    return Results.NotFound(TableNotFound(tableName));
                }

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
                LogSqliteException(app.Logger, ex, "patching record");
                return Results.BadRequest(new ErrorResponse("Unable to update record."));
            }
        });

        tables.MapDelete("/{tableName}/records/{id:int}", (string tableName, int id, IDatabase database) =>
        {
            try
            {
                if (!database.TableExists(tableName))
                {
                    return Results.NotFound(TableNotFound(tableName));
                }

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
                LogSqliteException(app.Logger, ex, "deleting record");
                return Results.BadRequest(new ErrorResponse("Unable to delete record."));
            }
        });
    }

    private static void LogSqliteException(ILogger logger, SQLiteException exception, string operation)
    {
        logger.LogWarning(exception, "SQLite operation failed while {Operation}.", operation);
    }

    private static string GetConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("RestDb")
            ?? configuration["RestDb:ConnectionString"]
            ?? Program.DefaultConnectionString;
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

public record CreateTableRequest(string Name, List<ColumnDefinition>? Columns);

public record ColumnDefinition(string Name, string Type);

public record RenameColumnRequest(string Name);

public record TableResponse(string Name, List<string> Columns);

public record PagedRecordsResponse(IReadOnlyList<Dictionary<string, object?>> Records, int Page, int PageSize, int TotalCount, int TotalPages);

public record ErrorResponse(string Error, string? Detail = null);
