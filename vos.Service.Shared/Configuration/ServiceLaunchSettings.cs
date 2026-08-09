using Microsoft.Extensions.Configuration;

namespace vos.Service.Shared.Configuration;

// The settings every service needs to start: the port it listens on, where the broker is, and the
// credentials for calling the broker and for checking what the broker sends back.
public record ServiceLaunchSettings(
    int Port,
    string MyceliumUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    public const int LowestPort = 1;
    public const int HighestPort = 65535;

    public static ServiceLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null) =>
        Parse(new LaunchSettingReader(arguments, configuration));

    // Null is the caller's signal to print the usage message and stop.
    public static ServiceLaunchSettings? Parse(LaunchSettingReader reader)
    {
        var myceliumUrl = reader.Read("myceliumUrl");
        if (string.IsNullOrEmpty(myceliumUrl))
            return null;

        if (!TryReadPort(reader, out var port))
            return null;

        return new ServiceLaunchSettings(
            port,
            myceliumUrl,
            reader.Read("token"),
            reader.Read("signingKey"),
            reader.Read("issuer"),
            reader.Read("audience"));
    }

    private static bool TryReadPort(LaunchSettingReader reader, out int port)
    {
        port = 0;
        var raw = reader.Read("port");
        return raw is not null
            && int.TryParse(raw, out port)
            && port is >= LowestPort and <= HighestPort;
    }

    private const string CommonFlagSummary =
        "--port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] " +
        "[--issuer=<issuer>] [--audience=<audience>]";

    private const string CommonFlagDescriptions =
        "  --port         Port number for the service to listen on\n" +
        "  --myceliumUrl  URL of the VOS Mycelium\n" +
        "  --token        Service JWT token for authenticating with Mycelium (optional)\n" +
        "  --signingKey   Base64-encoded signing key for validating mycelium requests (optional)\n" +
        "  --issuer       JWT issuer Mycelium signs with, which must match for /handle authentication\n" +
        "  --audience     JWT audience Mycelium signs with, which must match for /handle authentication";

    public static string UsageMessage => BuildUsageMessage();

    // A service with settings of its own passes them here rather than writing its own usage message,
    // so every service names the standard flags the same way.
    public static string BuildUsageMessage(
        string extraFlagSummary = "",
        string extraFlagDescriptions = "") =>
        $"Usage: dotnet run -- {CommonFlagSummary}{extraFlagSummary}\n" +
        $"{CommonFlagDescriptions}{extraFlagDescriptions}";
}
