using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestDb.Interfaces
{
    internal interface IDatabase
    {
        // Create a new record in the database
        void CreateRecord(string tableName, Dictionary<string, object> record);

        // Read records from the database
        List<Dictionary<string, object>> ReadRecords(string tableName);

        // Update a record in the database
        void UpdateRecord(string tableName, Dictionary<string, object> record);

        // Delete a record from the database
        void DeleteRecord(string tableName, int id);

        // Create a new table in the database
        void CreateTable(string tableName, List<string> columns);

        // Check to see if a table already exists
        public bool TableExists(string tableName);
    }
}
