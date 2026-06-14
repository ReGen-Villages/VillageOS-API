using Microsoft.Extensions.Configuration;

namespace vos.ManagedMicroservice.Metabolism.Configuration;

/// <summary>
/// Parsed command-line arguments for the Metabolism service.
/// </summary>
public record CliArgs(
    int Port,
    string MyceliumUrl,
    string Mode,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    /// <summary>
    /// Parses command-line arguments. Returns null if required args are missing or invalid.
    /// If <paramref name="config"/> is provided, any flag absent from <paramref name="args"/>
    /// falls back to <c>config[key]</c> — keys are flat: Port, MyceliumUrl, Mode, Token,
    /// SigningKey, Issuer, Audience. CLI args always take precedence over config.
    /// </summary>
    public static CliArgs? Parse(string[] args, IConfiguration? config = null)
    {
        string? FromArgsOrConfig(string flagPrefix, string configKey)
        {
            var fromArgs = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (fromArgs != null) return fromArgs.Substring(flagPrefix.Length);
            return config?[configKey];
        }

        var portStr = FromArgsOrConfig("--port=", "Port");
        var myceliumUrl = FromArgsOrConfig("--myceliumUrl=", "MyceliumUrl");
        var modeStr = FromArgsOrConfig("--mode=", "Mode");

        if (portStr == null || myceliumUrl == null || modeStr == null)
            return null;

        if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
            return null;

        var mode = modeStr.ToLowerInvariant();
        if (mode != "consumes" && mode != "produces")
            return null;

        var token = FromArgsOrConfig("--token=", "Token");
        var signingKey = FromArgsOrConfig("--signingKey=", "SigningKey");
        var issuer = FromArgsOrConfig("--issuer=", "Issuer");
        var audience = FromArgsOrConfig("--audience=", "Audience");

        return new CliArgs(port, myceliumUrl, mode, token, signingKey, issuer, audience);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --myceliumUrl=<url> --mode=<consumes|produces> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]\n" +
        "  --port       Port number for the service to listen on\n" +
        "  --myceliumUrl  URL of the VOS Mycelium\n" +
        "  --mode       Operation mode: 'consumes' (decrement) or 'produces' (increment)\n" +
        "  --token      Service JWT token for authenticating with Mycelium (optional)\n" +
        "  --signingKey Base64-encoded signing key for validating mycelium requests (optional)\n" +
        "  --issuer     JWT issuer Mycelium signs with — must match for /handle auth (Bug #5391)\n" +
        "  --audience   JWT audience Mycelium signs with — must match for /handle auth (Bug #5391)";
}
