namespace vos.ManagedMicroservice.Echo.Configuration;

/// <summary>
/// Parsed command-line arguments for the Echo endpoint service.
/// </summary>
public record CliArgs(
    int Port,
    string MyceliumUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    /// <summary>
    /// Parses command-line arguments. Returns null if required args are missing or invalid.
    /// </summary>
    public static CliArgs? Parse(string[] args)
    {
        var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
        var myceliumUrlArg = args.FirstOrDefault(a => a.StartsWith("--myceliumUrl="));
        var tokenArg = args.FirstOrDefault(a => a.StartsWith("--token="));
        var signingKeyArg = args.FirstOrDefault(a => a.StartsWith("--signingKey="));
        var issuerArg = args.FirstOrDefault(a => a.StartsWith("--issuer="));
        var audienceArg = args.FirstOrDefault(a => a.StartsWith("--audience="));

        if (portArg == null || myceliumUrlArg == null)
            return null;

        if (!int.TryParse(portArg.Substring("--port=".Length), out var port) || port < 1 || port > 65535)
            return null;

        var myceliumUrl = myceliumUrlArg.Substring("--myceliumUrl=".Length);
        var token = tokenArg?.Substring("--token=".Length);
        var signingKey = signingKeyArg?.Substring("--signingKey=".Length);
        var issuer = issuerArg?.Substring("--issuer=".Length);
        var audience = audienceArg?.Substring("--audience=".Length);

        return new CliArgs(port, myceliumUrl, token, signingKey, issuer, audience);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]\n" +
        "  --port       Port number for the service to listen on\n" +
        "  --myceliumUrl  URL of the VOS Mycelium\n" +
        "  --token      Service JWT token for authenticating with Mycelium (optional)\n" +
        "  --signingKey Base64-encoded signing key for validating mycelium requests (optional)\n" +
        "  --issuer     JWT issuer Mycelium signs with — must match for /handle auth (Bug #5391)\n" +
        "  --audience   JWT audience Mycelium signs with — must match for /handle auth (Bug #5391)";
}
