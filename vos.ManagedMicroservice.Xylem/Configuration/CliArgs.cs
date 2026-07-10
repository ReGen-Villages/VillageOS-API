namespace vos.ManagedMicroservice.Xylem.Configuration;

// Xylem draws external IFC models inward: it accepts an uploaded .ifc, runs the vos.Tools.IfcIngest
// tool, and applies the graph to Mycelium — so no client needs the ingest toolchain locally.
public record CliArgs(
    int Port,
    string MyceliumUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null,
    // Path to the built IfcIngest entry assembly (invoked via `dotnet <dll>`); lives in the private repo.
    string? IfcIngestDll = null,
    long MaxUploadBytes = 512L * 1024 * 1024)
{
    public static CliArgs? Parse(string[] args)
    {
        string? Val(string key) => args.FirstOrDefault(a => a.StartsWith(key))?.Substring(key.Length);

        var portRaw = Val("--port=");
        var myceliumUrl = Val("--myceliumUrl=");
        if (portRaw == null || myceliumUrl == null) return null;
        if (!int.TryParse(portRaw, out var port) || port < 1 || port > 65535) return null;

        var maxMbRaw = Val("--maxUploadMb=");
        var maxBytes = 512L * 1024 * 1024;
        if (maxMbRaw != null && long.TryParse(maxMbRaw, out var mb) && mb > 0) maxBytes = mb * 1024 * 1024;

        return new CliArgs(
            port, myceliumUrl,
            Token: Val("--token="),
            SigningKey: Val("--signingKey="),
            Issuer: Val("--issuer="),
            Audience: Val("--audience="),
            IfcIngestDll: Val("--ifcIngestDll="),
            MaxUploadBytes: maxBytes);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] " +
        "[--issuer=<iss>] [--audience=<aud>] [--ifcIngestDll=<path/to/vos.Tools.IfcIngest.dll>] [--maxUploadMb=<n>]";
}
