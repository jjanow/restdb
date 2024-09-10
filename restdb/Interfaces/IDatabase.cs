using System.Collections.Generic;

namespace RestDb.Interfaces
{
    public interface IDatabase
    {
        void CreateRecord(string tableName, Dictionary<string, object> record);
        List<Dictionary<string, object>> ReadRecords(string tableName);
        void UpdateRecord(string tableName, Dictionary<string, object> record);
        void DeleteRecord(string tableName, int id);
        void CreateTable(string tableName, List<string> columns);
        bool TableExists(string tableName);
    }
}