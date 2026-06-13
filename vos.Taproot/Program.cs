using vos.Taproot;
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
 __     ___ _ _                   ___  ____
 \ \   / (_) | | __ _  __ _  ___ / _ \/ ___|
  \ \ / /| | | |/ _` |/ _` |/ _ \ | | \___ \
   \ V / | | | | (_| | (_| |  __/ |_| |___) |
    \_/  |_|_|_|\__,_|\__, |\___|\___/|____/
                      |___/
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
