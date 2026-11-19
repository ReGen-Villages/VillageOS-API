using vos.Service.Shared.Configuration;

namespace vos.Service.Forage.Configuration;

public sealed record ForageLaunchSettings(ServiceLaunchSettings Service)
{
    // A provider that accepts the connection and then goes quiet is ended by Mycelium's limit on the
    // request it passes on. This is the backstop for a broker that does not answer at all: longer than
    // Mycelium's limit, short enough that such a broker cannot hold up a step.
    public static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(60);

    // How much of a source's window one planned call asks for. A year of hourly readings is a body of a
    // megabyte or so, reshaped in under a second and written as one set — the size a call was already
    // proven to carry. The reach itself is the model's to declare; how much of it one call takes is not.
    public const int SourceWindowDays = 365;

    public static ForageLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null) =>
        ServiceLaunchSettings.Parse(new LaunchSettingReader(arguments, configuration)) is { } service
            ? new ForageLaunchSettings(service)
            : null;

    // Mycelium's limit ends a lookup the fetching service is slow to answer, so the source timeout, which ends
    // one the broker never answers at all, has to outlast it.
    public void WarnWhenTheSourceTimeoutIsNotLongerThanThePlatformsLimit(ILogger<ForageLaunchSettings> logger)
    {
        if (Service.PassedOnRequestLimit is not { } limit || SourceTimeout > limit)
            return;

        logger.LogWarning(
            "Forage gives up on a lookup after {SourceTimeoutSeconds} seconds, which is not longer than the " +
            "{PassedOnRequestLimitSeconds} seconds Mycelium allows a call it passes on, so Forage can give up on a " +
            "lookup that is still being answered.",
            SourceTimeout.TotalSeconds, limit.TotalSeconds);
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage();
}
