using restdb.Classes;

namespace restdb
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Connect to the SQLite database
            string connectionString = "Data Source=database.db";
            Sqlite database = new Sqlite(connectionString);

            // Create a new record in the database
            Dictionary<string, object> newRecord = new Dictionary<string, object>()
        {
            { "name", "John" },
            { "age", 25 }
        };
            database.CreateRecord("people", newRecord);

            // Read records from the database
            List<Dictionary<string, object>> records = database.ReadRecords("people");
            foreach (Dictionary<string, object> record in records)
            {
                Console.WriteLine($"ID: {record["id"]}, Name: {record["name"]}, Age: {record["age"]}");
            }

            // Update a record in the database
            Dictionary<string, object> updatedRecord = new Dictionary<string, object>()
        {
            { "id", 1 },
            { "name", "John Smith" },
            { "age", 30 }
        };
            database.UpdateRecord("people", updatedRecord);

            // Delete a record from the database
            database.DeleteRecord("people", 2);
        }
    }
}