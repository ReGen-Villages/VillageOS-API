using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Intake.Configuration;

// The common service settings plus the origins the public form is served from and the endpoint the
// position lookups are forwarded through. No origin given leaves the list empty, which is a service
// that allows no cross-origin caller at all.
public sealed record IntakeLaunchSettings(
    ServiceLaunchSettings Service, string[] PublicFormOrigins, string FetcherSubdomain)
{
    /// <summary>The routing label the fetching service answers on, as the shipped analysis template
    /// declares it — the same default the discovery service uses, overridden the same way where a
    /// deployment points its fetches elsewhere.</summary>
    public const string DefaultFetcherSubdomain = "tributary";

    public static IntakeLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new IntakeLaunchSettings(
            service, ReadPublicFormOrigins(reader), ReadFetcherSubdomain(reader));
    }

    private static string[] ReadPublicFormOrigins(LaunchSettingReader reader) =>
        reader.Read("publicFormOrigin")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static string ReadFetcherSubdomain(LaunchSettingReader reader)
    {
        var raw = reader.Read("fetcherSubdomain");
        return string.IsNullOrWhiteSpace(raw) ? DefaultFetcherSubdomain : raw.Trim();
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--publicFormOrigin=<origin>[,<origin>]] [--fetcherSubdomain=<name>]" + MailDelivery.UsageSummary,
        "\n  --publicFormOrigin  Origin(s) of the public form allowed to call this service across origins"
        + "\n  --fetcherSubdomain  Endpoint service the position lookups are forwarded through"
        + MailDelivery.UsageMessage);
}
