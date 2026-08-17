using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Confluence.Configuration;

// The common service settings plus what a discovery run needs to bound itself: which endpoint
// service performs the fetch, how many sources may be in flight at once, and how long any one
// source may take.
public sealed record ConfluenceLaunchSettings(
    ServiceLaunchSettings Service,
    // The subdomain Mycelium forwards a fetch to. Configuration rather than a name in code: which
    // service fetches is a deployment's arrangement, and a service that names a sibling in a string
    // can only be pointed elsewhere by editing and redeploying it.
    string FetcherSubdomain,
    int MaxConcurrentSources,
    TimeSpan SourceTimeout)
{
    public const string DefaultFetcherSubdomain = "tributary";

    // Enough to keep a run brisk, low enough that a site covered by many sources cannot open a
    // burst of connections against public data portals that would read as abuse.
    public const int DefaultMaxConcurrentSources = 4;

    // A provider that accepts the connection and then goes quiet is more common than one that
    // refuses outright, and it is the failure most likely to be met in production. Longer than a
    // healthy source needs, short enough that one silent provider cannot hold up the run.
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(60);

    public static ConfluenceLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new ConfluenceLaunchSettings(
            service,
            ReadFetcherSubdomain(reader),
            ReadMaxConcurrentSources(reader),
            ReadSourceTimeout(reader));
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

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--fetcherSubdomain=<name>] [--maxConcurrentSources=<n>] [--sourceTimeoutSeconds=<n>]",
        "\n  --fetcherSubdomain      Endpoint service Mycelium forwards each fetch to" +
        "\n  --maxConcurrentSources  Sources in flight at once during one run" +
        "\n  --sourceTimeoutSeconds  Longest any one source may take before it is recorded unresolved");
}
