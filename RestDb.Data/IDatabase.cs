namespace RestDb.Data;

public interface IDatabase
{
    long CreateRecord(string tableName, Dictionary<string, object?> record);
    RecordReadResult ReadRecords(string tableName, RecordReadOptions? options = null);
    Dictionary<string, object?>? ReadRecord(string tableName, int id);
    bool UpdateRecord(string tableName, Dictionary<string, object?> record);
    bool DeleteRecord(string tableName, int id);
    bool CreateTable(string tableName, List<string> columns);
    bool AddColumn(string tableName, string column);
    bool RenameColumn(string tableName, string columnName, string newColumnName);
    bool DropColumn(string tableName, string columnName);
    bool TableExists(string tableName);
    IReadOnlyList<TableSummary> ListTables();
    TableSchema? GetTableSchema(string tableName);
}
