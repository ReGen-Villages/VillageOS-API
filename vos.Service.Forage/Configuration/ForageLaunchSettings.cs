using vos.Service.Shared.Configuration;

namespace vos.Service.Forage.Configuration;

public sealed record ForageLaunchSettings(
    ServiceLaunchSettings Service,
    // The subdomain Mycelium forwards a lookup to. Configuration rather than a name in code: which
    // service makes outside calls is a deployment's arrangement, and a service that names a sibling in a
    // string can only be pointed elsewhere by editing and redeploying it.
    string FetcherSubdomain,
    TimeSpan SourceTimeout,
    int SourceWindowDays)
{
    public const string DefaultFetcherSubdomain = "tributary";

    // A provider that accepts the connection and then goes quiet is ended by Mycelium's limit on the
    // request it passes on. This is the backstop for a broker that does not answer at all: longer than
    // Mycelium's limit, short enough that such a broker cannot hold up a step.
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(60);

    // How much of a source's window one planned call asks for. A year of hourly readings is a body of a
    // megabyte or so, reshaped in under a second and written as one set — the size a call was already
    // proven to carry. The reach itself is the model's to declare; how much of it this deployment
    // takes in one call is not.
    public const int DefaultSourceWindowDays = 365;

    public static ForageLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new ForageLaunchSettings(service, ReadFetcherSubdomain(reader), ReadSourceTimeout(reader), ReadSourceWindowDays(reader));
    }

    private static string ReadFetcherSubdomain(LaunchSettingReader reader)
    {
        var raw = reader.Read("fetcherSubdomain");
        return string.IsNullOrWhiteSpace(raw) ? DefaultFetcherSubdomain : raw.Trim();
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

    // Mycelium's limit ends a lookup the fetching service is slow to answer, so the source timeout, which ends
    // one the broker never answers at all, has to outlast it.
    public void WarnWhenTheSourceTimeoutIsNotLongerThanThePlatformsLimit(ILogger<ForageLaunchSettings> logger)
    {
        if (Service.PassedOnRequestLimit is not { } limit || SourceTimeout > limit)
            return;

        logger.LogWarning(
            "--sourceTimeoutSeconds is {SourceTimeoutSeconds} seconds, which is not longer than the " +
            "{PassedOnRequestLimitSeconds} seconds Mycelium allows a call it passes on, so Forage can give up on a " +
            "lookup that is still being answered. Set --sourceTimeoutSeconds above {PassedOnRequestLimitSeconds} seconds.",
            SourceTimeout.TotalSeconds, limit.TotalSeconds, limit.TotalSeconds);
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--fetcherSubdomain=<name>] [--sourceTimeoutSeconds=<n>] [--sourceWindowDays=<n>]",
        "\n  --fetcherSubdomain      Endpoint service Mycelium forwards each lookup to" +
        "\n  --sourceTimeoutSeconds  Longest any one lookup may take before it is given up" +
        "\n  --sourceWindowDays      How much of a source's window one planned call asks for");
}
