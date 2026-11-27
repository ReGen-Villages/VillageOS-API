using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed class DesignStrategyGroups(IReadOnlyDictionary<DesignStrategy, Guid> things)
{
    public Guid? ThingOf(DesignStrategy group) => things.TryGetValue(group, out var thing) ? thing : null;

    public static DesignStrategyGroups From(SnapshotDocument snapshot) => new(new Dictionary<DesignStrategy, Guid>());
}
