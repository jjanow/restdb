using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Collections.Generic;

namespace restdb.Classes
{
    internal class Sqlite : restdb.Interfaces.IDb
    {
        private SQLiteConnection connection;

        public Sqlite(string connectionString)
        {
            connection = new SQLiteConnection(connectionString);
        }

        public void CreateRecord(string tableName, Dictionary<string, object> record)
        {
            string query = $"INSERT INTO {tableName} ({string.Join(", ", record.Keys)}) VALUES ({string.Join(", ", record.Values)})";

            using (SQLiteCommand command = new SQLiteCommand(query, connection))
            {
                connection.Open();
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
    }
}