namespace vos.ManagedMicroservice.Phloem.Configuration;

/// <summary>Standard managed-microservice launch args (mirrors the other services). Phloem needs no
/// extra flags — the pipeline to run arrives per request, not at startup.</summary>
public record CliArgs(
    int Port,
    string MyceliumUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    /// <summary>Returns null if required args are missing or invalid.</summary>
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

        return new CliArgs(
            port,
            myceliumUrlArg.Substring("--myceliumUrl=".Length),
            tokenArg?.Substring("--token=".Length),
            signingKeyArg?.Substring("--signingKey=".Length),
            issuerArg?.Substring("--issuer=".Length),
            audienceArg?.Substring("--audience=".Length));
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]";
}
