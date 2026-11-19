using System.Text.Json;

namespace vos.Tests.Shared;

// A member's name is left out because it is data: a tool may meet the same word in any model.
public static class SeedVocabulary
{
    private static readonly Guid ConfigurationRole = new("0499de92-4e55-5861-be48-bec6bad357f1");
    private static readonly Guid OperationRole = new("bb3850d1-4436-5cca-8967-7af86674b349");
    private static readonly Guid PredicateRole = new("86743302-6588-5a93-b6a5-0dde78b092c1");
    private static readonly Guid RangeRole = new("c081de17-c325-55b7-99ca-16fb835ae545");
    private static readonly Guid KindOfEveryRoleThing = new("7e25da68-e710-5d0d-9d6e-ca34e6a16dcb");

    private static readonly string[] PredicatesThePlatformFindsByName = ["is", "has", "binds", "namesPropertyOf"];

    public static IReadOnlySet<string> NamesTheModelDeclaresForItself(string seedPath)
    {
        using var seed = JsonDocument.Parse(File.ReadAllText(seedPath));
        var things = seed.RootElement.GetProperty("Things").EnumerateArray()
            .ToDictionary(thing => thing.GetProperty("Id").GetGuid());
        var relationships = seed.RootElement.GetProperty("Relationships").EnumerateArray()
            .Select(each => (Subject: each.GetProperty("Subject").GetGuid(),
                Predicate: each.GetProperty("Predicate").GetGuid(),
                Target: each.GetProperty("Target").GetGuid()))
            .ToList();

        string NameOf(Guid id) => things[id].GetProperty("Name").GetString()!;
        var isA = relationships.Where(each => NameOf(each.Predicate) == "is").ToLookup(each => each.Subject, each => each.Target);
        var membersOf = relationships.Where(each => NameOf(each.Predicate) == "is").ToLookup(each => each.Target, each => each.Subject);
        var boundBy = relationships.Where(each => NameOf(each.Predicate) == "binds").ToLookup(each => each.Subject, each => each.Target);
        var bound = boundBy.SelectMany(targets => targets).ToHashSet();

        var roots = new[] { ConfigurationRole, OperationRole, PredicateRole }.SelectMany(role => boundBy[role]).ToHashSet();
        var predicateRoot = boundBy[PredicateRole].ToHashSet();
        var rangeKinds = boundBy[RangeRole].ToHashSet();
        var kindsThePlatformReads = bound.Except(roots).Append(KindOfEveryRoleThing).ToHashSet();

        bool IsUnder(Guid thing, HashSet<Guid> kinds, HashSet<Guid>? seen = null)
        {
            seen ??= [];
            return seen.Add(thing) && isA[thing].Any(above => kinds.Contains(above) || IsUnder(above, kinds, seen));
        }

        var kinds = things.Keys
            .Where(id => things[id].TryGetProperty("IsArchetype", out var isKind) && isKind.GetBoolean())
            .Where(id => !bound.Contains(id) && id != KindOfEveryRoleThing)
            .Where(id => !IsUnder(id, kindsThePlatformReads))
            .Where(id => !membersOf[id].Any(bound.Contains))
            .ToList();

        var predicates = things.Keys
            .Where(id => IsUnder(id, predicateRoot) && !bound.Contains(id))
            .Where(id => !PredicatesThePlatformFindsByName.Contains(NameOf(id)));

        var rangeThings = things.Keys.Where(id => isA[id].Any(rangeKinds.Contains));

        return kinds.Concat(predicates).Concat(rangeThings).Select(NameOf)
            .Concat(kinds.SelectMany(kind => KeysOf(things[kind], "Properties", "DerivedProperties", "OwnRanges")))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> KeysOf(JsonElement thing, params string[] sections) =>
        sections.SelectMany(section =>
            thing.TryGetProperty(section, out var held) && held.ValueKind == JsonValueKind.Object
                ? held.EnumerateObject().Select(entry => entry.Name)
                : []);
}
