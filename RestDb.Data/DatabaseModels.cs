namespace RestDb.Data;

public class TableNotFoundException : Exception
{
    public TableNotFoundException(string tableName)
        : base($"Table '{tableName}' was not found.") { }
}

public sealed record RecordReadOptions(
    int Page = 1,
    int PageSize = 50,
    string? FilterColumn = null,
    string? FilterValue = null,
    string? FilterOperator = null,
    string? SortColumn = null,
    string? SortDirection = null);

public sealed record RecordReadResult(
    IReadOnlyList<Dictionary<string, object?>> Records,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record TableSummary(string Name);

public sealed record TableSchema(
    string Name,
    IReadOnlyList<ColumnSchema> Columns);

public sealed record ColumnSchema(
    int Cid,
    string Name,
    string Type,
    bool NotNull,
    string? DefaultValue,
    bool PrimaryKey);
