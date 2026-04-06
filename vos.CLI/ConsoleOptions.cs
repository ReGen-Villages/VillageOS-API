namespace vos.CLI;

/// <summary>
/// Configuration options for the console application.
/// </summary>
public class ConsoleOptions
{
    private static readonly string[] BrokerUrlPrefixes = { "--broker-url=", "--broker=" };
    private static readonly string[] ApiKeyPrefixes = { "--api-key=", "--apikey=" };

    public string BrokerUrl { get; set; } = "https://localhost:7243";
    public string? ApiKey { get; set; }

    /// <summary>
    /// Parses command-line arguments and environment variables.
    /// Priority: command-line args > environment variables > defaults
    /// </summary>
    public static ConsoleOptions Parse(string[] args)
    {
        var options = new ConsoleOptions();
        ApplyEnvironmentVariables(options);
        ApplyCommandLineArgs(options, args);
        return options;
    }

    private static void ApplyEnvironmentVariables(ConsoleOptions options)
    {
        var envUrl = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            options.BrokerUrl = envUrl;

        var envKey = Environment.GetEnvironmentVariable("VOS_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            options.ApiKey = envKey;
    }

    private static void ApplyCommandLineArgs(ConsoleOptions options, string[] args)
    {
        foreach (var arg in args)
        {
            if (TryExtractValue(arg, BrokerUrlPrefixes, out var url))
                options.BrokerUrl = url;
            else if (TryExtractValue(arg, ApiKeyPrefixes, out var key))
                options.ApiKey = key;
        }
    }

    private static bool TryExtractValue(string arg, string[] prefixes, out string value)
    {
        foreach (var prefix in prefixes)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = arg.Substring(prefix.Length);
                return true;
            }
        }
        value = string.Empty;
        return false;
    }
}
