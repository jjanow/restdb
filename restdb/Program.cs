namespace RestDb;

public class Program
{
    public const string DefaultConnectionString = "Data Source=1database.db;Version=3;";

    public static async Task Main(string[] args)
    {
        Dictionary<string, string> switches = CliApplication.ParseSwitches(args);

        if (CliApplication.ShouldRun(switches))
        {
            CliApplication.Run(switches);
            return;
        }

        await RestApiApplication.RunAsync(args);
    }
}