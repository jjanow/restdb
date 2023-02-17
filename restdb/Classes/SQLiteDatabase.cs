using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Collections.Generic;

namespace RestDb.Classes
{
    internal class SQLiteDatabase : RestDb.Interfaces.IDatabase
    {
        private SQLiteConnection connection;

        public SQLiteDatabase(string connectionString)
        {
            connection = new SQLiteConnection(connectionString, true);
        }

        public void CreateRecord(string tableName, Dictionary<string, object> record)
        {
            List<string> columns = new List<string>(record.Keys);
            List<string> values = new List<string>();
            List<SQLiteParameter> parameters = new List<SQLiteParameter>();

            foreach (string column in columns)
            {
                values.Add($"@{column}");
                parameters.Add(new SQLiteParameter($"@{column}", record[column]));
            }

            string query = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.Parameters.AddRange(parameters.ToArray());
                command.ExecuteNonQuery();
                connection.Close();
            }
        }

        public List<Dictionary<string, object>> ReadRecords(string tableName)
        {
            string query = $"SELECT * FROM {tableName}";

            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();

                List<Dictionary<string, object>> records = new List<Dictionary<string, object>>();

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Dictionary<string, object> record = new Dictionary<string, object>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            record[reader.GetName(i)] = reader.GetValue(i);
                        }

                        records.Add(record);
                    }
                }

                connection.Close();

                return records;
            }
        }

        public void UpdateRecord(string tableName, Dictionary<string, object> record)
        {
            string query = $"UPDATE {tableName} SET {string.Join(", ", record.Select(x => $"{x.Key} = {x.Value}"))} WHERE id = {record["id"]}";

            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
                connection.Close();
            }
        }

        public void DeleteRecord(string tableName, int id)
        {
            string query = $"DELETE FROM {tableName} WHERE id = {id}";

            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
                connection.Close();
            }
        }

        public void CreateTable(string tableName, List<string> columns)
        {
            // Construct the CREATE TABLE query
            string query = "CREATE TABLE " + tableName + " (id INTEGER PRIMARY KEY AUTOINCREMENT";
            foreach (string column in columns)
            {
                query += ", " + column;
            }
            query += ")";

            // Execute the CREATE TABLE query
            SQLiteCommand command = new SQLiteCommand(query, connection);
            connection.Open();
            command.ExecuteNonQuery();
            connection.Close();
        }
    }
}