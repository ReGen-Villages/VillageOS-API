using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Intake.Configuration;

// The common service settings plus the origins the public form is served from. No origin given
// leaves the list empty, which is a service that allows no cross-origin caller at all.
public sealed record IntakeLaunchSettings(ServiceLaunchSettings Service, string[] PublicFormOrigins)
{
    public static IntakeLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new IntakeLaunchSettings(service, ReadPublicFormOrigins(reader));
    }

    private static string[] ReadPublicFormOrigins(LaunchSettingReader reader) =>
        reader.Read("publicFormOrigin")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--publicFormOrigin=<origin>[,<origin>]]" + MailDelivery.UsageSummary,
        "\n  --publicFormOrigin  Origin(s) of the public form allowed to call this service across origins"
        + MailDelivery.UsageMessage);
}
