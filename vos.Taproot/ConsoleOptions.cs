namespace vos.Taproot;

public class ConsoleOptions
{
    private static readonly string[] MyceliumUrlPrefixes = { "--mycelium-url=", "--mycelium=" };

    public string MyceliumUrl { get; set; } = "https://localhost:7243";
    public string? ApiKey { get; set; }

    // The API key is deliberately not a command-line setting: an argument list is readable by every
    // process on the host and the shell keeps it in its history file. VOS_API_KEY is its only source.
    // Precedence for everything else: command-line args > environment variables > defaults.
    public static ConsoleOptions Parse(string[] args)
    {
        var options = new ConsoleOptions();
        ApplyEnvironmentVariables(options);
        ApplyCommandLineArgs(options, args);
        return options;
    }

    private static void ApplyEnvironmentVariables(ConsoleOptions options)
    {
        var envUrl = Environment.GetEnvironmentVariable("VOS_MYCELIUM_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            options.MyceliumUrl = envUrl;

        var envKey = Environment.GetEnvironmentVariable("VOS_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            options.ApiKey = envKey;
    }

    private static void ApplyCommandLineArgs(ConsoleOptions options, string[] args)
    {
        foreach (var arg in args)
        {
            if (TryExtractValue(arg, MyceliumUrlPrefixes, out var url))
                options.MyceliumUrl = url;
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
