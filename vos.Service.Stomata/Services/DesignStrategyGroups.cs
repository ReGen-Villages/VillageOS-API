using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed record DesignStrategyGroup(Guid Id, string? Name, IReadOnlyDictionary<string, bool> Tests);

public sealed class DesignStrategyGroups(
    IReadOnlyList<DesignStrategyGroup> tried,
    IReadOnlyList<DesignStrategyGroup> unordered,
    IReadOnlyList<IReadOnlyList<DesignStrategyGroup>> sharingAnOrder)
{
    public const string OrderProperty = "order";

    public IReadOnlyList<DesignStrategyGroup> Tried { get; } = tried;

    public IReadOnlyList<DesignStrategyGroup> Unordered { get; } = unordered;

    public IReadOnlyList<IReadOnlyList<DesignStrategyGroup>> SharingAnOrder { get; } = sharingAnOrder;

    public DesignStrategyGroup? Of(HourTests hour) => null;

    public static DesignStrategyGroups From(SnapshotDocument snapshot) => new([], [], []);
}
