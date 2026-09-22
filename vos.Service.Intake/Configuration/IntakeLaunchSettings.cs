using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Intake.Configuration;

// The common service settings plus the origins the public form is served from and the endpoint the
// position lookups are forwarded through. No origin given leaves the list empty, which is a service
// that allows no cross-origin caller at all.
public sealed record IntakeLaunchSettings(
    ServiceLaunchSettings Service, string[] PublicFormOrigins, string FetcherSubdomain, string DocumentDirectory)
{
    // Where the files a submitter shares are kept, keyed by submission, when no folder is
    // given: beside the service, so a deployment that never shares a file configures nothing.
    public const string DefaultDocumentDirectory = "documents";

    // The routing label the fetching service answers on, as the shipped analysis template
    // declares it — the same default the discovery service uses, overridden the same way where a
    // deployment points its fetches elsewhere.
    public const string DefaultFetcherSubdomain = "tributary";

    public static IntakeLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new IntakeLaunchSettings(
            service, ReadPublicFormOrigins(reader), ReadFetcherSubdomain(reader), ReadDocumentDirectory(reader));
    }

    private static string[] ReadPublicFormOrigins(LaunchSettingReader reader) =>
        reader.Read("publicFormOrigin")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static string ReadDocumentDirectory(LaunchSettingReader reader)
    {
        var raw = reader.Read("documentDirectory");
        return string.IsNullOrWhiteSpace(raw) ? DefaultDocumentDirectory : raw.Trim();
    }

    private static string ReadFetcherSubdomain(LaunchSettingReader reader)
    {
        var raw = reader.Read("fetcherSubdomain");
        return string.IsNullOrWhiteSpace(raw) ? DefaultFetcherSubdomain : raw.Trim();
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--publicFormOrigin=<origin>[,<origin>]] [--fetcherSubdomain=<name>] [--documentDirectory=<path>]" + MailDelivery.UsageSummary,
        "\n  --publicFormOrigin  Origin(s) of the public form allowed to call this service across origins"
        + "\n  --fetcherSubdomain  Endpoint service the position lookups are forwarded through"
        + "\n  --documentDirectory Folder the files submitters share are kept in, beside the service unless rooted"
        + MailDelivery.UsageMessage);
}
