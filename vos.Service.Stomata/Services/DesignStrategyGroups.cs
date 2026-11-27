using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed class DesignStrategyGroups(IReadOnlyDictionary<DesignStrategy, Guid> things)
{
    private static readonly IReadOnlyDictionary<string, DesignStrategy> ByName = new Dictionary<string, DesignStrategy>
    {
        ["comfortable-as-it-is"] = DesignStrategy.ComfortableAsItIs,
        ["with-air-movement"] = DesignStrategy.WithAirMovement,
        ["dehumidification-needed"] = DesignStrategy.DehumidificationNeeded,
        ["heating-needed"] = DesignStrategy.HeatingNeeded,
        ["direct-evaporative-cooling"] = DesignStrategy.DirectEvaporativeCooling,
        ["two-stage-evaporative-cooling"] = DesignStrategy.TwoStageEvaporativeCooling,
        ["cooling-system-needed"] = DesignStrategy.CoolingSystemNeeded,
    };

    public Guid? ThingOf(DesignStrategy group) => things.TryGetValue(group, out var thing) ? thing : null;

    // A model's names need not be unique, so a name two members share picks out neither.
    public static DesignStrategyGroups From(SnapshotDocument snapshot) => new(
        RoleMembers.Of(snapshot, StomataRoles.DesignStrategyGroup)
            .Where(member => member.Name is not null && ByName.ContainsKey(member.Name))
            .GroupBy(member => ByName[member.Name!])
            .Where(named => named.Count() == 1)
            .ToDictionary(named => named.Key, named => named.Single().Id));
}
