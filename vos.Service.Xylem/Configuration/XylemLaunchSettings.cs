using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Xylem.Configuration;

// The common service settings plus where the ingest tool lives and how large an upload Xylem will
// take. Xylem runs that tool over an uploaded .ifc and applies the result to Mycelium, so no client
// needs the ingest toolchain locally.
public sealed record XylemLaunchSettings(
    ServiceLaunchSettings Service,
    // Path to the built IfcIngest entry assembly (invoked via `dotnet <dll>`); lives in the private repo.
    string? IfcIngestDll,
    long MaxUploadBytes)
{
    public const long DefaultMaxUploadBytes = 512L * 1024 * 1024;
    private const long BytesPerMegabyte = 1024 * 1024;

    public static XylemLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new XylemLaunchSettings(
            service,
            reader.Read("ifcIngestDll"),
            ReadMaxUploadBytes(reader));
    }

    private static long ReadMaxUploadBytes(LaunchSettingReader reader)
    {
        var raw = reader.Read("maxUploadMb");
        return raw is not null && long.TryParse(raw, out var megabytes) && megabytes > 0
            ? megabytes * BytesPerMegabyte
            : DefaultMaxUploadBytes;
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--ifcIngestDll=<path/to/vos.Tools.IfcIngest.dll>] [--maxUploadMb=<n>]",
        "\n  --ifcIngestDll Path to the built IfcIngest entry assembly" +
        "\n  --maxUploadMb  Largest upload accepted, in megabytes");
}
