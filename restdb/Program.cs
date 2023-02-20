using RestDb.Classes;
using System.Data.SQLite;

namespace RestDb
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Parse command-line arguments into a dictionary
            Dictionary<string, string> switches = new Dictionary<string, string>()
            {
                { "-createtable", "" },
                { "-createrecord", "" },
                { "-readrecord", "" },
                { "-updaterecord", "" },
                { "-deleterecord", "" },
            };

            for (int i = 0; i < args.Length; i += 2)
            {
                if (switches.ContainsKey(args[i]))
                {
                    switches[args[i]] = args[i + 1];
                }
            }

            // Set up the SQLite database connection
            string connectionString = "Data Source=1database.db;Version=3;";
            SQLiteDatabase database = new SQLiteDatabase(connectionString);

            // Perform the appropriate action based on the command-line arguments
            if (!string.IsNullOrEmpty(switches["-createtable"]))
            {
                string tableName = switches["-createtable"];
                List<string> columns = new List<string>() { "name TEXT", "age INTEGER" };
                database.CreateTable(tableName, columns);
                Console.WriteLine($"Created table {tableName} with columns {string.Join(", ", columns)}");
            }
            else if (!string.IsNullOrEmpty(switches["-createrecord"]))
            {
                string tableName = switches["-createrecord"];
                Dictionary<string, object> record = new Dictionary<string, object>()
                {
                    { "name", "John Doe" },
                    { "age", 42 }
                };
                database.CreateRecord(tableName, record);
                Console.WriteLine($"Created record in table {tableName} with values {string.Join(", ", record)}");
            }
            else if (!string.IsNullOrEmpty(switches["-readrecord"]))
            {
                string tableName = switches["-readrecord"];
                List<Dictionary<string, object>> records = database.ReadRecords(tableName);
                Console.WriteLine($"Read {records.Count} records from table {tableName}:");
                foreach (var record in records)
                {
                    Console.WriteLine(string.Join(", ", record));
                }
            }
            else if (!string.IsNullOrEmpty(switches["-updaterecord"]))
            {
                string tableName = switches["-updaterecord"];
                Dictionary<string, object> record = new Dictionary<string, object>()
                {
                    { "id", 1 },
                    { "name", "Jane Smith" },
                    { "age", 30 }
                };
                database.UpdateRecord(tableName, record);
                Console.WriteLine($"Updated record in table {tableName} with id {record["id"]} to values {string.Join(", ", record)}");
            }
            else if (!string.IsNullOrEmpty(switches["-deleterecord"]))
            {
                string tableName = switches["-deleterecord"];
                int id = 1;
                database.DeleteRecord(tableName, id);
                Console.WriteLine($"Deleted record with id {id} from table {tableName}");
            }
            else
            {
                Console.WriteLine("Usage: yourprogram.exe -createtable tablename -createrecord tablename -readrecord tablename -updaterecord tablename -deleterecord tablename");
            }

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
}