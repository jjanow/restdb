# restdb

`restdb` is an early-stage .NET REST API for experimenting with SQLite CRUD
operations behind a small database abstraction.

The project now runs as an ASP.NET Core minimal API by default and keeps the
original command-line CRUD workflow available when `-operation` is supplied.

## Project Status

This repository is a work in progress. The core REST and CLI paths are present,
with a small data library shared by both entry points.

What exists today:

- A .NET 9 ASP.NET Core minimal API in `restdb/`.
- A `RestDb.Data` class library containing the database abstraction and SQLite
  implementation.
- REST endpoints for table creation and record insert/read/update/delete.
- Swagger/OpenAPI documentation at `/swagger`.
- Optional API-key enforcement through `RestDb:ApiKey`.
- Paged, filterable, and sortable record reads.
- Schema inspection plus add, rename, and drop column migration endpoints and
  CLI commands.
- CLI handling for table creation and record insert/read/update/delete.
- Automated API tests in `RestDb.Tests/`.

Known gaps in the current implementation:

- Table schemas are intentionally small and accept only SQLite `TEXT`,
  `INTEGER`, `REAL`, `BLOB`, and `NUMERIC` column types.
- Table and column names are dynamic, but limited to simple SQL identifiers.
- API-key enforcement is intentionally small and should be replaced or extended
  before production exposure.
- Schema migrations are limited to SQLite column-level changes; complex table
  rewrites, indexes, and constraints are not managed yet.

## Repository Layout

```text
.
├── README.md
├── restdb.sln
├── RestDb.Data/
│   ├── DatabaseModels.cs
│   ├── IDatabase.cs
│   └── SQLiteDatabase.cs
├── RestDb.Tests/
│   ├── RestDb.Tests.csproj
│   └── UnitTest1.cs
└── restdb/
    ├── Program.cs
    ├── restdb.csproj
    ├── appsettings.json
    ├── Properties/
    │   └── launchSettings.json
```

## Requirements

- .NET SDK 9.0 or newer.
- SQLite support through the NuGet packages declared in
  `RestDb.Data/RestDb.Data.csproj`.

The project currently restores:

- `System.Data.SQLite` for the data library
- `Swashbuckle.AspNetCore` for Swagger/OpenAPI
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

API-key enforcement is disabled unless `RestDb:ApiKey` is configured. When set,
all API routes except `/health` and `/swagger` require an `X-API-Key` header:

```bash
RestDb__ApiKey="local-dev-key" dotnet run --project restdb/restdb.csproj
```

## OpenAPI

Run the API and open:

```text
http://localhost:5055/swagger
```

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
operator is `eq`, and the default sort is `id` ascending.

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

- `Program.cs` contains the REST API routes plus the CLI fallback path.
- `RestDb.Data/IDatabase.cs` defines the database operations.
- `RestDb.Data/SQLiteDatabase.cs` implements those operations for SQLite.
- `RestDb.Tests/UnitTest1.cs` contains HTTP integration tests.

Useful local checks:

```bash
dotnet build restdb.sln
dotnet test restdb.sln
```

## Suggested Next Steps

Good follow-up improvements would be:

- Replace or extend the simple API-key check with a production-ready
  authentication and authorization model.
- Add schema management for indexes, constraints, and table rewrites that SQLite
  cannot express as simple column-level migrations.
- Validate requested filter and sort columns against table schemas before
  executing record reads.
- Split the REST and CLI entry points if they continue to grow independently.
