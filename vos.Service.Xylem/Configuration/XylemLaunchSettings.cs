using vos.Service.Shared.Configuration;

namespace vos.Service.Xylem.Configuration;

// The common service settings plus where the ingest tool lives. Xylem runs that tool over an uploaded
// .ifc and applies the result to Mycelium, so no client needs the ingest toolchain locally.
public sealed record XylemLaunchSettings(
    ServiceLaunchSettings Service,
    // Path to the built ModelIngest entry assembly (invoked via `dotnet <dll>`); lives in the private repo.
    string? ModelIngestDll)
{
    public const long MaxUploadBytes = 512L * 1024 * 1024;

    public static XylemLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new XylemLaunchSettings(service, reader.Read("modelIngestDll"));
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--modelIngestDll=<path/to/vos.Tools.ModelIngest.dll>]",
        "\n  --modelIngestDll Path to the built ModelIngest entry assembly");
}
