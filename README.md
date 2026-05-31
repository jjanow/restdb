# restdb

`restdb` is an early-stage .NET REST API for experimenting with SQLite CRUD
operations behind a small database abstraction.

The project now runs as an ASP.NET Core minimal API by default and keeps the
command-line CRUD workflow available through a separate CLI entry point when
`-operation` is supplied.

## Project Status

This repository is a work in progress. The core REST and CLI paths are present,
with a small data library shared by both entry points.

What exists today:

- A .NET 10 ASP.NET Core minimal API in `restdb/`.
- A `RestDb.Data` class library containing the database abstraction and SQLite
  implementation.
- REST endpoints for table creation and record insert/read/update/delete.
- Swagger/OpenAPI documentation at `/swagger`.
- Optional API-key authentication and authorization through `RestDb:ApiKey`.
- Paged, filterable, and sortable record reads with schema validation for
  requested filter and sort columns.
- Schema inspection plus add, rename, and drop column migration endpoints and
  CLI commands.
- CLI handling for table creation and record insert/read/update/delete.
- Automated API tests in `RestDb.Tests/`.
- OpenTelemetry metrics exported at `/metrics` for Prometheus scraping, with a
  Docker Compose stack for Prometheus and Grafana.

Known gaps in the current implementation:

- Table schemas are intentionally small and accept only SQLite `TEXT`,
  `INTEGER`, `REAL`, `BLOB`, and `NUMERIC` column types.
- Table and column names are dynamic, but limited to simple SQL identifiers.
- Schema migrations are limited to SQLite column-level changes; complex table
  rewrites, indexes, and constraints are not managed yet.

## Repository Layout

```text
.
├── README.md
├── restdb.sln
├── docker-compose.yml
├── prometheus.yml
├── grafana/
│   └── provisioning/
│       ├── datasources/
│       │   └── prometheus.yml
│       └── dashboards/
│           ├── restdb.yml
│           └── restdb-dashboard.json
├── RestDb.Data/
│   ├── DatabaseModels.cs
│   ├── IDatabase.cs
│   └── SQLiteDatabase.cs
├── RestDb.Tests/
│   ├── RestDb.Tests.csproj
│   └── UnitTest1.cs
└── restdb/
    ├── ApiKeyAuthentication.cs
    ├── CliApplication.cs
    ├── Program.cs
    ├── RestApiApplication.cs
    ├── restdb.csproj
    ├── appsettings.json
    ├── Properties/
    │   └── launchSettings.json
```

## Requirements

- .NET SDK 10.0 or newer.
- SQLite support through the NuGet packages declared in
  `RestDb.Data/RestDb.Data.csproj`.

The project currently restores:

- `System.Data.SQLite` and `SQLitePCLRaw.lib.e_sqlite3` for the data library
- `Swashbuckle.AspNetCore` for Swagger/OpenAPI
- `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Instrumentation.Runtime`, and
  `OpenTelemetry.Exporter.Prometheus.AspNetCore` for observability
- `Microsoft.AspNetCore.Mvc.Testing` for the integration test project

## Build

```bash
dotnet build restdb.sln
```

## Run

Run the REST API:

```bash
dotnet run --project restdb/restdb.csproj
```

By default, the API listens on the profile URL in
`restdb/Properties/launchSettings.json` during local development.

The default SQLite connection string is configured in `restdb/appsettings.json`:

```text
Data Source=1database.db;Version=3;
```

That creates or reads `1database.db` relative to the working directory where the
application is launched.

Override it with a standard ASP.NET Core connection string setting:

```bash
ConnectionStrings__RestDb="Data Source=/tmp/restdb.db;Version=3;" \
  dotnet run --project restdb/restdb.csproj
```

API-key authentication is disabled unless `RestDb:ApiKey` is configured. When
set, table and record routes require an authenticated API client. `/health` and
`/swagger` stay public. Clients can send either an `X-API-Key` header or a
standard bearer token:

```bash
RestDb__ApiKey="local-dev-key" dotnet run --project restdb/restdb.csproj
curl http://localhost:5055/tables -H "Authorization: Bearer local-dev-key"
```

## OpenAPI

Run the API and open:

```text
http://localhost:5055/swagger
```

## Observability

The API exposes a Prometheus-compatible metrics endpoint at `/metrics`. It is
public (no API key required) so Prometheus can scrape it without authentication.

Metrics collected:

| Metric | Description |
|--------|-------------|
| `restdb_database_operation_duration_ms` | Histogram of SQLite operation durations (ms), tagged by `operation` (`CreateRecord`, `ReadRecords`, `ReadRecord`, `UpdateRecord`, `DeleteRecord`, `CreateTable`, `TableExists`, `ListTables`, `GetTableSchema`, `AddColumn`, `RenameColumn`, `DropColumn`) |
| `http_server_request_duration_seconds` | ASP.NET Core HTTP request duration, tagged by route, method, and status code |
| `process_runtime_dotnet_*` | .NET runtime metrics: GC heap, thread pool queue length, active threads, etc. |

### Prometheus + Grafana (Docker Compose)

A ready-to-run stack is included. With the API running on port 5055:

```bash
docker compose up -d
```

- Prometheus: <http://localhost:9090>
- Grafana: <http://localhost:3000> (login: `admin` / `admin`)

The Prometheus datasource and a RestDb dashboard are provisioned automatically.
The dashboard shows HTTP request rate and p99 latency, per-operation database
durations, and .NET runtime health panels.

The `prometheus.yml` scrape config uses `host.docker.internal:5055` to reach
the API running on the host. On Linux this requires Docker Engine 20.10+ with
host networking, or you can replace `host.docker.internal` with the host's IP
address.

## REST API Reference

### Health

```bash
curl http://localhost:5055/health
```

### List Tables

```bash
curl http://localhost:5055/tables
```

### Create a Table

```bash
curl -X POST http://localhost:5055/tables \
  -H "Content-Type: application/json" \
  -d '{
    "name": "users",
    "columns": [
      { "name": "name", "type": "TEXT" },
      { "name": "age", "type": "INTEGER" },
      { "name": "job", "type": "TEXT" }
    ]
  }'
```

### Create a Record

```bash
curl -X POST http://localhost:5055/tables/users/records \
  -H "Content-Type: application/json" \
  -d '{ "name": "Alice", "age": 31, "job": "Engineer" }'
```

### Read Records

```bash
curl http://localhost:5055/tables/users/records
curl http://localhost:5055/tables/users/records/1
```

Record collection reads return paging metadata:

```json
{
  "records": [],
  "page": 1,
  "pageSize": 50,
  "totalCount": 0,
  "totalPages": 0
}
```

Use `page`, `pageSize`, `filterColumn`, `filterValue`, `filterOperator`,
`sortColumn`, and `sortDirection` query parameters for paging, filtering, and
sorting:

```bash
curl "http://localhost:5055/tables/users/records?page=1&pageSize=25&filterColumn=job&filterValue=Engineer&sortColumn=name&sortDirection=asc"
curl "http://localhost:5055/tables/users/records?filterColumn=name&filterOperator=contains&filterValue=Ali"
```

Supported filter operators are `eq`, `ne`, `contains`, `startsWith`,
`endsWith`, `gt`, `gte`, `lt`, `lte`, `isNull`, and `isNotNull`. The default
operator is `eq`, and the default sort is `id` ascending. `filterColumn` and
`sortColumn` must reference columns that exist on the requested table.

### Inspect Table Schema

```bash
curl http://localhost:5055/tables/users/schema
```

### Add a Column

```bash
curl -X POST http://localhost:5055/tables/users/columns \
  -H "Content-Type: application/json" \
  -d '{ "name": "email", "type": "TEXT" }'
```

### Rename a Column

```bash
curl -X POST http://localhost:5055/tables/users/columns/email/rename \
  -H "Content-Type: application/json" \
  -d '{ "name": "contact_email" }'
```

### Drop a Column

```bash
curl -X DELETE http://localhost:5055/tables/users/columns/contact_email
```

### Update a Record

```bash
curl -X PUT http://localhost:5055/tables/users/records/1 \
  -H "Content-Type: application/json" \
  -d '{ "job": "Architect" }'
```

`PATCH` is also supported for the same route shape.

### Delete a Record

```bash
curl -X DELETE http://localhost:5055/tables/users/records/1
```

## CLI Reference

The CLI parser accepts dash-prefixed switches in `-key value` pairs.

### Create a Table

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation create \
  -table users \
  -columns name:text,age:integer,job:text
```

Column definitions are comma-separated and each column uses `name:type` format.
The implementation automatically adds an `id INTEGER PRIMARY KEY AUTOINCREMENT`
column.

### Insert a Record

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation insert \
  -table users \
  -name Alice \
  -age 31 \
  -job Engineer
```

### Read Records

Read all records from a table:

```bash
dotnet run --project restdb/restdb.csproj -- -operation read -table users
```

Read a filtered page:

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation read \
  -table users \
  -page 1 \
  -pagesize 25 \
  -filtercolumn job \
  -filtervalue Engineer \
  -sortcolumn name \
  -sortdirection asc
```

Use `-filteroperator` for non-equality filters:

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation read \
  -table users \
  -filtercolumn name \
  -filteroperator contains \
  -filtervalue Ali
```

`-filtercolumn` and `-sortcolumn` must reference columns that exist on the
requested table.

Read one record by `id`:

```bash
dotnet run --project restdb/restdb.csproj -- -operation read -table users -id 1
```

### Update a Record

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation update \
  -table users \
  -id 1 \
  -name Alice \
  -job Engineer
```

### Delete a Record

```bash
dotnet run --project restdb/restdb.csproj -- -operation delete -table users -id 1
```

### List Tables

```bash
dotnet run --project restdb/restdb.csproj -- -operation tables
```

### Inspect Table Schema

```bash
dotnet run --project restdb/restdb.csproj -- -operation schema -table users
```

### Add a Column

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation add-column \
  -table users \
  -column email:text
```

### Rename a Column

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation rename-column \
  -table users \
  -column email \
  -newname contact_email
```

### Drop a Column

```bash
dotnet run --project restdb/restdb.csproj -- \
  -operation drop-column \
  -table users \
  -column contact_email
```

## Development Notes

- `Program.cs` chooses the REST or CLI entry point.
- `RestApiApplication.cs` configures the REST API host, routes, and OTel metrics.
- `CliApplication.cs` handles command-line CRUD operations.
- `ApiKeyAuthentication.cs` contains the ASP.NET Core authentication handler
  and authorization policy for protected API routes.
- `RestDb.Data/IDatabase.cs` defines the database operations.
- `RestDb.Data/SQLiteDatabase.cs` implements those operations for SQLite, with
  per-method duration histograms via `System.Diagnostics.Metrics`.
- `RestDb.Tests/UnitTest1.cs` contains HTTP integration tests including a
  `/metrics` smoke test.
- `docker-compose.yml` + `prometheus.yml` bring up a local Prometheus + Grafana
  stack.
- `grafana/provisioning/` auto-provisions the Prometheus datasource and a
  RestDb dashboard.

Useful local checks:

```bash
dotnet build restdb.sln
dotnet test restdb.sln
```