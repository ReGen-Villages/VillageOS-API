using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed record DesignStrategyGroup(Guid Id, string? Name, IReadOnlyDictionary<string, bool> Tests)
{
    public string Label => Name ?? Id.ToString();
}

public sealed class DesignStrategyGroups(
    IReadOnlyList<DesignStrategyGroup> tried,
    IReadOnlyList<DesignStrategyGroup> unordered,
    IReadOnlyList<IReadOnlyList<DesignStrategyGroup>> sharingAnOrder)
{
    public const string OrderProperty = "order";

    public IReadOnlyList<DesignStrategyGroup> Tried { get; } = tried;

    public IReadOnlyList<DesignStrategyGroup> Unordered { get; } = unordered;

    public IReadOnlyList<IReadOnlyList<DesignStrategyGroup>> SharingAnOrder { get; } = sharingAnOrder;

    public DesignStrategyGroup? Of(HourTests hour) =>
        Tried.FirstOrDefault(group => group.Tests.All(test => hour.Holds(test.Key) == test.Value));

    public static DesignStrategyGroups From(SnapshotDocument snapshot)
    {
        var members = RoleMembers.Of(snapshot, StomataRoles.DesignStrategyGroup)
            .Select(member => (Group: new DesignStrategyGroup(member.Id, member.Name, TestsOf(member)), Order: OrderOf(member)))
            .ToList();
        var ordered = members.Where(member => member.Order is not null)
            .OrderBy(member => member.Order)
            .ThenBy(member => member.Group.Id)
            .ToList();
        return new DesignStrategyGroups(
            [.. ordered.Select(member => member.Group)],
            [.. members.Where(member => member.Order is null).Select(member => member.Group)],
            [.. ordered.GroupBy(member => member.Order).Where(placed => placed.Count() > 1)
                .Select(placed => (IReadOnlyList<DesignStrategyGroup>)[.. placed.Select(member => member.Group)])]);
    }

    private static Dictionary<string, bool> TestsOf(SnapshotThing member) => HourTests.Names
        .Where(test => member.Properties.TryGetValue(test, out var held) && held.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        .ToDictionary(test => test, test => member.Properties[test].Value.GetBoolean());

    private static long? OrderOf(SnapshotThing member) =>
        member.Properties.TryGetValue(OrderProperty, out var held) && held.Value.ValueKind == JsonValueKind.Number ? held.Value.GetInt64() : null;
}
