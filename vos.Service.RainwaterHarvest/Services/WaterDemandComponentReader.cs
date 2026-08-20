using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.RainwaterHarvest.Services;

/// <summary>One demand the harvest serves, as the model declares it: where its size is read from, where
/// its answers go, and where it stands in the queue for the water.</summary>
public sealed record WaterDemandComponent(
    string Name,
    long ServingOrder,
    string QuantityProperty,
    string RateProperty,
    string DemandProperty,
    string CoverageProperty,
    string ShortfallProperty);

// Which demands a harvest serves and the order it serves them in, read from the model rather than held
// here. One harvest serves all of them, so which is served first is part of the answer — and a site that
// waters a crop before it drinks is then a template edit rather than a change to this service.
//
// Every demand is a quantity times a rate, so a component carries no formula of its own and a third one
// needs nothing added here.
//
// Selected by the mark, never by the archetype's name: a model that renamed the archetype would answer
// with an empty vocabulary, and the harvest would apportion nothing with nothing saying so.
public static class WaterDemandComponentReader
{
    public const string ComponentArchetypeFlag = "__IsWaterDemandComponentArchetype";

    private const string ServingOrderField = "servingOrder";
    private const string QuantityPropertyField = "demandQuantityProperty";
    private const string RatePropertyField = "demandRateProperty";
    private const string DemandPropertyField = "demandProperty";
    private const string CoveragePropertyField = "coverageProperty";
    private const string ShortfallPropertyField = "shortfallProperty";

    /// <summary>Reaches the components without an edge to follow: they hang under their archetype and
    /// nothing relates them to a study, so the selection is by the mark that archetype carries.</summary>
    public static SubscriptionSelector Selector => new()
    {
        MarkedTypes = [ComponentArchetypeFlag],
        IncludeRelationships = false,
    };

    public static IReadOnlyList<WaterDemandComponent> Read(SnapshotDocument snapshot)
    {
        // The selection answers with the archetype as well as its members, and the archetype carries the
        // empty declarations every member states over. Reading it as a component would put a demand of
        // nothing at the front of the queue and write the answers nowhere.
        var declared = snapshot.Things.Where(thing => !thing.IsArchetype).ToList();

        if (declared.Count == 0)
            throw new InvalidOperationException(
                "This model declares no water demand component, so there is nothing for the harvest to be "
                + $"measured against. A component is a Thing whose type carries '{ComponentArchetypeFlag}'.");

        return [.. declared.Select(Component).OrderBy(component => component.ServingOrder)];
    }

    private static WaterDemandComponent Component(SnapshotThing thing) => new(
        thing.Name ?? thing.Id.ToString(),
        Order(thing),
        Names(thing, QuantityPropertyField), Names(thing, RatePropertyField),
        Names(thing, DemandPropertyField), Names(thing, CoveragePropertyField), Names(thing, ShortfallPropertyField));

    private static long Order(SnapshotThing thing)
    {
        var stated = Effective(thing, ServingOrderField)?.Value;
        return stated?.ValueKind switch
        {
            JsonValueKind.Number => stated.Value.GetInt64(),
            _ => throw new InvalidOperationException(Refusal(thing, ServingOrderField,
                "which has to be a whole number, because it is this demand's place in the queue for the "
                + "harvest and two demands cannot both be served first")),
        };
    }

    private static string Names(SnapshotThing thing, string field)
    {
        var named = Effective(thing, field)?.Value;
        var property = named?.ValueKind == JsonValueKind.String ? named.Value.GetString() : null;

        return string.IsNullOrWhiteSpace(property)
            ? throw new InvalidOperationException(Refusal(thing, field,
                "which has to name a property on the study. Left empty, this demand would be read off "
                + "nothing or written nowhere, and the study would hold half an answer"))
            : property;
    }

    /// <summary>Own first, then what the Thing states over the type it `is`. A member that states a value
    /// over its archetype's declaration carries it as an override rather than as its own — seed
    /// normalization relocates it there — so a reader of own properties alone finds every component
    /// blank.</summary>
    private static SnapshotProperty? Effective(SnapshotThing thing, string name)
    {
        if (thing.Properties.TryGetValue(name, out var own)) return own;

        foreach (var stated in thing.InheritedProperties.Values)
            if (stated.Properties.TryGetValue(name, out var over)) return over;

        return null;
    }

    private static string Refusal(SnapshotThing thing, string field, string why) =>
        $"The water demand component '{thing.Name ?? thing.Id.ToString()}' states "
        + $"'{field}' as {Effective(thing, field)?.Value.ToString() ?? "nothing at all"}, {why}.";
}
