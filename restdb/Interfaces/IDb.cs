using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace restdb.Interfaces
{
    internal interface IDb
    {
        // Create a new record in the database
        void CreateRecord(string tableName, Dictionary<string, object> record);

        // Read records from the database
        List<Dictionary<string, object>> ReadRecords(string tableName);

        // Update a record in the database
        void UpdateRecord(string tableName, Dictionary<string, object> record);

        // Delete a record from the database
        void DeleteRecord(string tableName, int id);
    }
}
