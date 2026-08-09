using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Metabolism.Configuration;

// The common service settings plus the direction this instance moves a quantity in.
public sealed record MetabolismLaunchSettings(ServiceLaunchSettings Service, string Mode)
{
    public const string ConsumesMode = "consumes";
    public const string ProducesMode = "produces";

    public static MetabolismLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        var mode = reader.Read("mode")?.ToLowerInvariant();
        if (mode is not (ConsumesMode or ProducesMode))
            return null;

        return new MetabolismLaunchSettings(service, mode);
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        $" --mode=<{ConsumesMode}|{ProducesMode}>",
        $"\n  --mode         Whether the service decrements ({ConsumesMode}) or increments ({ProducesMode})");
}
