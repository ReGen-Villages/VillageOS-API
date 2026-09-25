using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Forage.Configuration;

// The common service settings plus what a discovery run needs to bound itself: which endpoint
// service performs the fetch, how many sources may be in flight at once, and how long any one
// source may take.
public sealed record ForageLaunchSettings(
    ServiceLaunchSettings Service,
    // The subdomain Mycelium forwards a fetch to. Configuration rather than a name in code: which
    // service fetches is a deployment's arrangement, and a service that names a sibling in a string
    // can only be pointed elsewhere by editing and redeploying it.
    string FetcherSubdomain,
    int MaxConcurrentSources,
    TimeSpan SourceTimeout,
    int SourceWindowDays)
{
    public const string DefaultFetcherSubdomain = "tributary";

    // Enough to keep a run brisk, low enough that a site covered by many sources cannot open a
    // burst of connections against public data portals that would read as abuse.
    public const int DefaultMaxConcurrentSources = 4;

    // A provider that accepts the connection and then goes quiet is more common than one that
    // refuses outright, and it is the failure most likely to be met in production. Longer than a
    // healthy source needs, short enough that one silent provider cannot hold up the run.
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(60);

    // How much of a source's window one fetch asks for. A year of hourly readings is a body of a
    // megabyte or so, reshaped in under a second and written as one set — the size a run was already
    // proven to carry. The reach itself is the model's to declare; how much of it this deployment
    // takes in one pass is not.
    public const int DefaultSourceWindowDays = 365;

    public static ForageLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new ForageLaunchSettings(
            service,
            ReadFetcherSubdomain(reader),
            ReadMaxConcurrentSources(reader),
            ReadSourceTimeout(reader),
            ReadSourceWindowDays(reader));
    }

    private static string ReadFetcherSubdomain(LaunchSettingReader reader)
    {
        var raw = reader.Read("fetcherSubdomain");
        return string.IsNullOrWhiteSpace(raw) ? DefaultFetcherSubdomain : raw.Trim();
    }

    private static int ReadMaxConcurrentSources(LaunchSettingReader reader)
    {
        var raw = reader.Read("maxConcurrentSources");
        return raw is not null && int.TryParse(raw, out var value) && value > 0
            ? value
            : DefaultMaxConcurrentSources;
    }

    private static TimeSpan ReadSourceTimeout(LaunchSettingReader reader)
    {
        var raw = reader.Read("sourceTimeoutSeconds");
        return raw is not null && int.TryParse(raw, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultSourceTimeout;
    }

    private static int ReadSourceWindowDays(LaunchSettingReader reader)
    {
        var raw = reader.Read("sourceWindowDays");
        return raw is not null && int.TryParse(raw, out var days) && days > 0
            ? days
            : DefaultSourceWindowDays;
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--fetcherSubdomain=<name>] [--maxConcurrentSources=<n>] [--sourceTimeoutSeconds=<n>]"
        + " [--sourceWindowDays=<n>]",
        "\n  --fetcherSubdomain      Endpoint service Mycelium forwards each fetch to" +
        "\n  --maxConcurrentSources  Sources in flight at once during one run" +
        "\n  --sourceTimeoutSeconds  Longest any one source may take before it is recorded unresolved" +
        "\n  --sourceWindowDays      How much of a source's window one fetch asks for");
}
