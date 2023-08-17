using RestDb.Classes;
using System;
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

            // Perform CRUD operation based on command line switches
            switch (switches.ContainsKey("-operation") ? switches["-operation"] : "")
            {
                case "create":
                    string tableName = switches["-table"];
                    List<string> columns = switches.ContainsKey("-columns") ? switches["-columns"].Split(',').ToList() : new List<string>();

                    database.CreateTable(tableName, columns);
                    Console.WriteLine($"Table '{tableName}' created.");
                    break;

                case "read":
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
                        {
                            Console.WriteLine("Invalid ID specified.");
                            return;
                        }

                        //Dictionary<string, object> record = database.ReadRecords(switches["-table"]);
                        Dictionary<string, object> record = database.ReadRecord(switches["-table"], id);
                        if (record != null)
                        {
                            Console.WriteLine($"Record ID: {id}");
                            foreach (KeyValuePair<string, object> kvp in record)
                            {
                                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Record with ID {id} not found.");
                        }
                    }
                    else
                    {
                        //List<Dictionary<string, object>> records = database.ReadAllRecords(switches["-table"]);
                        List<Dictionary<string, object>> records = database.ReadRecords(switches["-table"]);
                        if (records != null && records.Count > 0)
                        {
                            Console.WriteLine($"Table: {switches["-table"]}");
                            foreach (Dictionary<string, object> record in records)
                            {
                                Console.WriteLine($"Record ID: {record["id"]}");
                                foreach (KeyValuePair<string, object> kvp in record)
                                {
                                    if (kvp.Key != "id")
                                    {
                                        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                                    }
                                }
                                Console.WriteLine("");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"No records found in table {switches["-table"]}.");
                        }
                    }
                    break;

                case "update":
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
                        {
                            Console.WriteLine("Invalid ID specified.");
                            return;
                        }
                        Dictionary<string, object> recordToUpdate = new Dictionary<string, object> { { "id", id } };
                        foreach (KeyValuePair<string, string> kvp in switches.Where(x => !x.Key.StartsWith("-")))
                        {
                            recordToUpdate[kvp.Key] = kvp.Value;
                        }
                        database.UpdateRecord(switches["-table"], recordToUpdate);
                        Console.WriteLine($"Record with ID {id} updated.");
                    }
                    else
                    {
                        Console.WriteLine("ID is required to update a record.");
                    }
                    break;

                case "delete":
                    if (switches.ContainsKey("-id"))
                    {
                        int id = 0;
                        if (!Int32.TryParse(switches["-id"], out id))
                        {
                            Console.WriteLine("Invalid ID specified.");
                            return;
                        }
                        database.DeleteRecord(switches["-table"], id);
                        Console.WriteLine($"Record with ID {id} deleted.");
                    }
                    else
                    {
                        Console.WriteLine("ID not specified.");
                    }
                    break;
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