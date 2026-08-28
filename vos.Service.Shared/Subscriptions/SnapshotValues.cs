using System.Text.Json;

namespace vos.Service.Shared.Subscriptions;

/// <summary>What a Thing in a snapshot says for itself, as against what its type says for it.
///
/// <para>A value written for a name the Thing's archetype declares is not stored as an own property: the
/// model clones the declaration into an override under that archetype's id and writes there, so the
/// instance keeps its own value without touching the type. The snapshot carries the two apart, which
/// leaves a reader of own properties alone finding every figure a submission wrote absent — a parcel's
/// measured area, an allocation's share, a coverage's attempt count — while the same figure reads back
/// perfectly well over the REST routes, which resolve.</para>
///
/// <para>Own first, then the override sets, and never up the `is` chain: the question a reader asks of a
/// snapshot is whether this Thing states this, and a Thing that merely inherits a value has stated
/// nothing. That is what keeps an archetype's members from answering with the mark the archetype
/// carries.</para></summary>
public static class SnapshotValues
{
    public static SnapshotProperty? StatedValue(this SnapshotThing thing, string name) =>
        StatedIn(thing.Properties, thing.InheritedOverrides, name);

    /// <summary>Whether the Thing itself carries a mark. Asked of what it states rather than of what the
    /// `is` chain resolves, because a mark is an ordinary property and so is inherited: a resolving reader
    /// would answer with every member of the archetype as well as the archetype, leaving nothing to select
    /// a vocabulary or a role by.</summary>
    public static bool CarriesFlag(this SnapshotThing thing, string flag) =>
        thing.StatedValue(flag) is { } marked && marked.Value.ValueKind == JsonValueKind.True;

    /// <summary>Every value the Thing states, for a reader that takes what it finds rather than asking for
    /// a name it already knows. Own properties answer first, so a name stated twice is read once.</summary>
    public static IEnumerable<KeyValuePair<string, SnapshotProperty>> ValuesStated(this SnapshotThing thing)
    {
        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stated in thing.Properties)
            if (answered.Add(stated.Key)) yield return stated;

        if (thing.InheritedOverrides is null) yield break;
        foreach (var stated in ValuesIn(thing.InheritedOverrides))
            if (answered.Add(stated.Key)) yield return stated;
    }

    private static IEnumerable<KeyValuePair<string, SnapshotProperty>> ValuesIn(
        Dictionary<string, InheritedPropertySet> sets)
    {
        foreach (var set in sets.Values)
        {
            foreach (var stated in set.Properties) yield return stated;
            if (set.Inherited is { } deeper)
                foreach (var stated in ValuesIn(deeper)) yield return stated;
        }
    }

    private static SnapshotProperty? StatedIn(
        Dictionary<string, SnapshotProperty> own,
        Dictionary<string, InheritedPropertySet>? overrides,
        string name) =>
        own.TryGetValue(name, out var stated) ? stated
        : overrides is null ? null
        : InSets(overrides, name);

    // The first set holding the name answers. A name two sources supply is one the model refuses to
    // resolve by name at all, so no order here would be more right than another.
    private static SnapshotProperty? InSets(Dictionary<string, InheritedPropertySet> sets, string name)
    {
        foreach (var set in sets.Values)
        {
            if (set.Properties.TryGetValue(name, out var stated)) return stated;
            if (set.Inherited is { } deeper && InSets(deeper, name) is { } further) return further;
        }

        return null;
    }
}
