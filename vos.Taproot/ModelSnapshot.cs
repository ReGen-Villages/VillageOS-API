using System.Text.Json;

namespace vos.Taproot;

// Everything a model-walking command reads, taken once. Properties are read effective rather
// than own: seed normalization moves a Thing's own values into its overrides, and a reader looking only
// at own properties finds a model full of Things and reads nothing off them.
//
// Things arrive as a list and are held by identifier, because every question asked of one here is
// asked while walking; searching the list for each would read the whole model once per step, so the cost
// of a walk would grow with the size of the model around it.
public sealed record ModelSnapshot(
    IReadOnlyDictionary<Guid, JsonElement> ThingsById, JsonElement Relationships, JsonElement Properties)
{
    public static async Task<ModelSnapshot> ReadAsync(MyceliumClient client) => new(
        ModelReading.ByIdentifier(await client.GetAllThingsAsync()),
        await client.GetAllRelationshipsAsync(),
        await client.GetAllPropertiesAsync("effective"));
}

// Reading Things, relationships and role bindings off a ModelSnapshot. Nothing here names an
// archetype or a predicate: a role is found through the role Thing that binds it.
public static class ModelReading
{
    // The platform's own predicates, and the only predicate names a reader may hold: they are the
    // platform's vocabulary rather than any model's, and nothing marks or binds them.
    public const string IsPredicateName = "is";
    public const string BindsPredicateName = "binds";

    // The Thing a role binds, read from the role Thing's own `binds` relationship, never from a name. A role
    // the model binds to nothing, or that it binds twice, answers with none.
    public static Guid? BoundBy(ModelSnapshot model, Guid roleId)
    {
        if (OneNamed(model, BindsPredicateName) is not { } binds) return null;
        var bound = EdgesThrough(model, binds).Where(edge => Subject(edge) == roleId).Select(Target).ToList();
        return bound.Count == 1 ? bound[0] : null;
    }

    // The one Thing under a name, which is what the platform's own predicates are found by.
    public static Guid? OneNamed(ModelSnapshot model, string name)
    {
        var named = model.ThingsById.Keys.Where(id => NameOf(model, id) == name).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    // The first Thing under each identifier, which is what searching the list found before.
    public static IReadOnlyDictionary<Guid, JsonElement> ByIdentifier(JsonElement things)
    {
        var byIdentifier = new Dictionary<Guid, JsonElement>();
        foreach (var thing in things.EnumerateArray())
            byIdentifier.TryAdd(Identifier(thing, "Id"), thing);

        return byIdentifier;
    }

    public static IEnumerable<JsonElement> EdgesThrough(ModelSnapshot model, Guid predicate) =>
        model.Relationships.EnumerateArray().Where(edge => Predicate(edge) == predicate);

    public static Guid Subject(JsonElement edge) => Identifier(edge, "SubjectId");

    public static Guid Predicate(JsonElement edge) => Identifier(edge, "PredicateId");

    public static Guid Target(JsonElement edge) => Identifier(edge, "TargetId");

    public static Guid Identifier(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetGuid(out var id) ? id : Guid.Empty;

    public static string? NameOf(ModelSnapshot model, Guid id) =>
        model.ThingsById.TryGetValue(id, out var thing) && thing.TryGetProperty("Name", out var name)
            ? name.GetString()
            : null;

    public static bool IsArchetype(ModelSnapshot model, Guid id) =>
        model.ThingsById.TryGetValue(id, out var thing)
        && thing.TryGetProperty("IsArchetype", out var archetype)
        && archetype.ValueKind == JsonValueKind.True;

    // A key's name without the archetype that declared it, which is how the same property reads
    // whether a Thing holds it or inherits it.
    public static string DeclaredName(string key) => key[(key.LastIndexOf('.') + 1)..];

    // A property as text, whatever it is written as, because everything here is displayed.
    //
    // A Thing's own value is keyed by the bare name, but a value it holds for a name its archetype
    // declares comes back keyed by that archetype — Submission.submittedAt rather than
    // submittedAt. Both are the same property to a reader, so the name is matched after its
    // declaring prefix.
    //
    // A Thing cannot own a name and inherit the same one, so at most one key can match — except where
    // the name is inherited from more than one archetype. The model answers a bare read of that with an
    // ambiguity and asks for the full path; a list has no path to give, so it says which paths it found
    // rather than showing a reader a value the model itself declines to choose.
    public static string? Value(ModelSnapshot model, Guid thing, string property)
    {
        if (!model.Properties.TryGetProperty(thing.ToString(), out var properties)) return null;

        var matching = properties.EnumerateObject()
            .Where(held => DeclaredName(held.Name) == property)
            .ToList();

        if (matching.Count > 1)
            throw new InvalidOperationException(
                $"'{property}' is inherited from more than one archetype on {thing}, so reading it by that "
                + $"name alone says nothing: {string.Join(", ", matching.Select(held => held.Name))}. "
                + "Read it by its full path.");

        if (matching.Count == 0) return null;
        var held = matching[0].Value;

        var value = held.TryGetProperty("Value", out var inner) ? inner : held;
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.ToString(),
        };
    }
}
