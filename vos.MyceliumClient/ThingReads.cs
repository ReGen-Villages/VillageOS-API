using System.Net;
using System.Text.Json;

namespace vos.Taproot;

public sealed record ThingRead(Guid Id, JsonElement? Thing, string? Refusal);

public sealed record InheritedSource(Guid Id, string Name, IReadOnlyList<InheritedSource> InheritsFrom);

public sealed record PropertyHeld(
    string Name, JsonElement Value, string Type, bool IsInherited, Guid? InheritedFrom, string? InheritedFromName);

public sealed record ThingInWhole(
    Guid Id, string Name, bool IsArchetype, IReadOnlyList<InheritedSource> InheritsFrom,
    IReadOnlyList<PropertyHeld> Properties, IReadOnlyList<string> CurrentStates);

public sealed record PropertyAtPath(
    string? Path, JsonElement? Value, string? Type, bool IsInherited, Guid? InheritedFrom, IReadOnlyList<string>? MatchingPaths)
{
    public static PropertyAtPath From(JsonElement answered) => new(
        answered.GetStringOrDefault("Path", ""), answered.GetProperty("Value").Clone(), answered.GetStringOrDefault("Type", ""),
        answered.IsTrue("IsInherited"), answered.GetNullableGuid("InheritedFrom"), null);

    public static PropertyAtPath MatchingMoreThanOne(IReadOnlyList<string> paths) => new(null, null, null, false, null, paths);
}

public static class ThingReads
{
    public static async Task<ThingRead> OneAsync(MyceliumClient mycelium, string nameOrId)
    {
        var found = await new NameResolver(mycelium).FindThingAsync(nameOrId);
        return found.IsSuccess
            ? new ThingRead(found.Id, await mycelium.GetThingAsync(found.Id), null)
            : new ThingRead(Guid.Empty, null, found.ErrorMessage);
    }

    // Null where the Thing cannot be read, including one removed after it was first read.
    public static async Task<ThingInWhole?> WholeAsync(MyceliumClient mycelium, Guid id, JsonElement thing)
    {
        var properties = UnlessNotFound(mycelium.GetPropertiesAsync(id));
        var sources = UnlessNotFound(mycelium.GetInheritedPropertiesAsync(id));
        var states = UnlessNotFound(mycelium.GetStatesAsync(id));
        await Task.WhenAll(properties, sources, states);
        if (await properties is not { } held || await sources is not { } inherited || await states is not { } current)
            return null;

        var inheritsFrom = inherited.EnumerateArray().Select(SourceOf).ToList();
        var sourceNames = new Dictionary<Guid, string>();
        NameEach(inheritsFrom, sourceNames);

        return new ThingInWhole(
            id,
            thing.GetStringOrDefault("Name", ""),
            thing.IsTrue("IsArchetype"),
            inheritsFrom,
            held.EnumerateObject().Select(property => HeldOf(property, sourceNames)).ToList(),
            current.TryGetProperty("CurrentStates", out var named)
                ? named.EnumerateArray().Select(state => state.GetString() ?? "").ToList()
                : []);
    }

    private static InheritedSource SourceOf(JsonElement source) => new(
        source.GetProperty("SourceId").GetGuid(),
        source.GetStringOrDefault("SourceName", ""),
        source.TryGetProperty("Inherited", out var nested) ? nested.EnumerateArray().Select(SourceOf).ToList() : []);

    private static void NameEach(IEnumerable<InheritedSource> sources, Dictionary<Guid, string> names)
    {
        foreach (var source in sources)
        {
            names[source.Id] = source.Name;
            NameEach(source.InheritsFrom, names);
        }
    }

    private static PropertyHeld HeldOf(JsonProperty property, IReadOnlyDictionary<Guid, string> sourceNames)
    {
        var from = property.Value.GetNullableGuid("InheritedFrom");
        return new PropertyHeld(
            property.Name,
            property.Value.GetProperty("Value").Clone(),
            property.Value.GetStringOrDefault("Type", ""),
            property.Value.IsTrue("IsInherited"),
            from,
            from is { } source && sourceNames.TryGetValue(source, out var name) ? name : null);
    }

    private static async Task<JsonElement?> UnlessNotFound(Task<JsonElement> read)
    {
        try
        {
            return await read;
        }
        catch (HttpRequestException refused) when (refused.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
