using System.Collections.Generic;

namespace RestDb.Interfaces
{
    public interface IDatabase
    {
        long CreateRecord(string tableName, Dictionary<string, object?> record);
        List<Dictionary<string, object?>> ReadRecords(string tableName);
        Dictionary<string, object?>? ReadRecord(string tableName, int id);
        bool UpdateRecord(string tableName, Dictionary<string, object?> record);
        bool DeleteRecord(string tableName, int id);
        bool CreateTable(string tableName, List<string> columns);
        bool TableExists(string tableName);
    }
}