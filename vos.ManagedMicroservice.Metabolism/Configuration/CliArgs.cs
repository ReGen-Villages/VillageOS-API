namespace vos.ManagedMicroservice.Metabolism.Configuration;

/// <summary>
/// Parsed command-line arguments for the Metabolism service.
/// </summary>
public record CliArgs(int Port, string BrokerUrl, string Mode, string? Token = null, string? SigningKey = null)
{
    /// <summary>
    /// Parses command-line arguments. Returns null if required args are missing or invalid.
    /// </summary>
    public static CliArgs? Parse(string[] args)
    {
        var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
        var brokerUrlArg = args.FirstOrDefault(a => a.StartsWith("--brokerUrl="));
        var modeArg = args.FirstOrDefault(a => a.StartsWith("--mode="));
        var tokenArg = args.FirstOrDefault(a => a.StartsWith("--token="));
        var signingKeyArg = args.FirstOrDefault(a => a.StartsWith("--signingKey="));

        if (portArg == null || brokerUrlArg == null || modeArg == null)
            return null;

        if (!int.TryParse(portArg.Substring("--port=".Length), out var port) || port < 1 || port > 65535)
            return null;

        var brokerUrl = brokerUrlArg.Substring("--brokerUrl=".Length);
        var mode = modeArg.Substring("--mode=".Length).ToLowerInvariant();

        if (mode != "consumes" && mode != "produces")
            return null;

        var token = tokenArg?.Substring("--token=".Length);
        var signingKey = signingKeyArg?.Substring("--signingKey=".Length);

        return new CliArgs(port, brokerUrl, mode, token, signingKey);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --brokerUrl=<url> --mode=<consumes|produces> [--token=<jwt>] [--signingKey=<base64>]\n" +
        "  --port       Port number for the service to listen on\n" +
        "  --brokerUrl  URL of the Ducati Broker\n" +
        "  --mode       Operation mode: 'consumes' (decrement) or 'produces' (increment)\n" +
        "  --token      Service JWT token for authenticating with the broker (optional)\n" +
        "  --signingKey Base64-encoded signing key for validating broker requests (optional)";
}
