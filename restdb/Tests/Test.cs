using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestDb.Tests
{
    internal class Test
    {
        //// Create a new table in the database
        //string tableName = "employees";

        //if (!database.TableExists(tableName))
        //{
        //    List<string> columns = new List<string>() { "name TEXT", "title TEXT", "salary INT" };
        //    database.CreateTable(tableName, columns);
        //}

        //// Insert some data into the new table
        //Dictionary<string, object> newRecord = new Dictionary<string, object>()
        //{
        //    { "name", "Alice" },
        //    { "title", "Manager" },
        //    { "salary", 50000 }
        //};
        //database.CreateRecord(tableName, newRecord);

        //// Read the data from the new table
        //List<Dictionary<string, object>> records = database.ReadRecords(tableName);
        //foreach (Dictionary<string, object> record in records)
        //{
        //    Console.WriteLine($"ID: {record["id"]}, Name: {record["name"]}, Title: {record["title"]}, Salary: {record["salary"]}");
        //}

        //// Update a record in the new table
        //Dictionary<string, object> updatedRecord = new Dictionary<string, object>()
        //{
        //    { "id", 1 },
        //    { "name", "Alice Smith" },
        //    { "title", "Senior Manager" },
        //    { "salary", 60000 }
        //};
        //database.UpdateRecord(tableName, updatedRecord);

        //// Delete a record from the new table
        //database.DeleteRecord(tableName, 1);
    }
}
