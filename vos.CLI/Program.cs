using vos.CLI;
using Serilog;

internal class Program
{
    private static async Task Main(string[] args)
    {
        // Configure Serilog
        var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "cli-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "CLI")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();

        try
        {
            Console.WriteLine(@"
  ____  _   _  ____    _  _____ ___
 |  _ \| | | |/ ___|  / \|_   _|_ _|
 | | | | | | | |     / _ \ | |  | |
 | |_| | |_| | |___ / ___ \| |  | |
 |____/ \___/ \____/_/   \_\_| |___|

              C L I
");

            var options = ConsoleOptions.Parse(args);
            var brokerClient = new BrokerClient(options.BrokerUrl, options.ApiKey);
            var handler = new CommandHandler(Console.In, Console.Out, brokerClient, options.BrokerUrl, interactiveMode: true);
            await handler.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "CLI terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
