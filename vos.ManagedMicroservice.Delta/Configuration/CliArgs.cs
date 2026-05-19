namespace vos.ManagedMicroservice.Delta.Configuration;

/// <summary>
/// Parsed command-line arguments for the Delta service.
/// </summary>
public record CliArgs(
    int Port,
    string BrokerUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    /// <summary>
    /// Parses command-line arguments. Returns null if required args are missing or invalid.
    /// Falls back to DELTA_PORT / DELTA_BROKER_URL / DELTA_TOKEN / DELTA_SIGNING_KEY /
    /// DELTA_ISSUER / DELTA_AUDIENCE environment variables for any flag not present in args.
    /// This enables WebApplicationFactory&lt;Program&gt;-based tests to inject config via env vars
    /// without parsing synthetic CLI args. Production callers continue to pass --flag=value as
    /// before; behavior is unchanged when all required flags are present in args.
    /// </summary>
    public static CliArgs? Parse(string[] args)
    {
        string? FromArgsOrEnv(string flagPrefix, string envVar)
        {
            var fromArgs = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (fromArgs != null) return fromArgs.Substring(flagPrefix.Length);
            return Environment.GetEnvironmentVariable(envVar);
        }

        var portStr = FromArgsOrEnv("--port=", "DELTA_PORT");
        var brokerUrl = FromArgsOrEnv("--brokerUrl=", "DELTA_BROKER_URL");

        if (portStr == null || brokerUrl == null)
            return null;

        if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
            return null;

        var token = FromArgsOrEnv("--token=", "DELTA_TOKEN");
        var signingKey = FromArgsOrEnv("--signingKey=", "DELTA_SIGNING_KEY");
        var issuer = FromArgsOrEnv("--issuer=", "DELTA_ISSUER");
        var audience = FromArgsOrEnv("--audience=", "DELTA_AUDIENCE");

        return new CliArgs(port, brokerUrl, token, signingKey, issuer, audience);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --brokerUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]\n" +
        "  --port       Port number for the service to listen on\n" +
        "  --brokerUrl  URL of the VOS Broker\n" +
        "  --token      Service JWT token for authenticating with the broker (optional)\n" +
        "  --signingKey Base64-encoded signing key for validating broker requests (optional)\n" +
        "  --issuer     JWT issuer the broker signs with — must match for /handle auth (Bug #5391)\n" +
        "  --audience   JWT audience the broker signs with — must match for /handle auth (Bug #5391)";
}
