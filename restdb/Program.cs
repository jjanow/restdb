using RestDb.Classes;
using System.Data.Entity;
using System.Data.SQLite;
using System.Collections.Generic;
using System.Linq;

namespace RestDb
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Parse command line switches
            Dictionary<string, string> switches = ParseSwitches(args);

            // Create a new SQLite database instance
            SQLiteDatabase database = new SQLiteDatabase("Data Source=1database.db;Version=3;");

            // Create table if -createtable switch was passed
            if (switches.ContainsKey("-createtable"))
            {
                string tableName = switches["-createtable"];
                List<string> columns = switches.ContainsKey("-columns") ? switches["-columns"].Split(',').ToList() : new List<string>();

                database.CreateTable(tableName, columns);
            }
        }

        private static Dictionary<string, string> ParseSwitches(string[] args)
        {
            Dictionary<string, string> switches = new Dictionary<string, string>();

            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].ToLowerInvariant();

                if (key.StartsWith("-") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    string value = args[i + 1];
                    switches[key] = value;
                    i++;
                }
            }

            return switches;
        }
    }
}