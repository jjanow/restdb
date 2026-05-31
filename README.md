# restdb

`restdb` is an early-stage .NET REST API for experimenting with SQLite CRUD
operations behind a small database abstraction.

The project now runs as an ASP.NET Core minimal API by default and keeps the
original command-line CRUD workflow available when `-operation` is supplied.

## Project Status

This repository is a work in progress. The core pieces are present, but not all
CRUD paths are wired through the CLI yet.

What exists today:

- A .NET 9 ASP.NET Core minimal API in `restdb/`.
- An `IDatabase` interface for basic CRUD-style operations.
- A `SQLiteDatabase` implementation backed by `System.Data.SQLite`.
- REST endpoints for table creation and record insert/read/update/delete.
- CLI handling for table creation and record insert/read/update/delete.
- Automated API tests in `RestDb.Tests/`.

Known gaps in the current implementation:

- Table schemas are intentionally small and accept only SQLite `TEXT`,
  `INTEGER`, `REAL`, `BLOB`, and `NUMERIC` column types.
- Table and column names are dynamic, but limited to simple SQL identifiers.
- There is no authentication, authorization, pagination, filtering, or schema
  migration support yet.

## Repository Layout

```text
.
├── README.md
├── restdb.sln
├── RestDb.Tests/
│   ├── RestDb.Tests.csproj
│   └── UnitTest1.cs
└── restdb/
    ├── Program.cs
    ├── restdb.csproj
    ├── appsettings.json
    ├── Classes/
    │   └── SQLiteDatabase.cs
    ├── Interfaces/
    │   └── IDatabase.cs
    ├── Properties/
    │   └── launchSettings.json
```

## Requirements

- .NET SDK 9.0 or newer.
- SQLite support through the NuGet packages declared in `restdb/restdb.csproj`.

The project currently restores:

- `System.Data.SQLite`
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

## REST API Reference

### Health

```bash
curl http://localhost:5055/health
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

## Development Notes

- `Program.cs` contains the REST API routes plus the CLI fallback path.
- `Interfaces/IDatabase.cs` defines the database operations.
- `Classes/SQLiteDatabase.cs` implements those operations for SQLite.
- `RestDb.Tests/UnitTest1.cs` contains HTTP integration tests.

Useful local checks:

```bash
dotnet build restdb.sln
dotnet test restdb.sln
```

## Suggested Next Steps

Good follow-up improvements would be:

- Add OpenAPI/Swagger documentation for the REST surface.
- Add authentication and authorization before exposing the API beyond local
  experimentation.
- Add pagination, filtering, and richer error responses for record reads.
- Add schema inspection and migration endpoints or commands.
- Decide whether to split the database abstraction into a separate class
  library if the CLI and API continue to grow independently.
