using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Metabolism.Configuration;

// The common service settings plus the direction this instance moves a quantity in.
public sealed record MetabolismLaunchSettings(ServiceLaunchSettings Service, ResourceDirection Direction)
{
    public static MetabolismLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        var direction = ResourceDirection.Parse(reader.Read("mode"));
        if (direction is null)
            return null;

        return new MetabolismLaunchSettings(service, direction);
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        $" --mode=<{string.Join("|", ResourceDirection.All.Select(d => d.LaunchArgument))}>",
        $"\n  --mode         Whether the service {ResourceDirection.Consumes.PoolAction} "
        + $"({ResourceDirection.Consumes}) or {ResourceDirection.Produces.PoolAction} ({ResourceDirection.Produces})");
}
