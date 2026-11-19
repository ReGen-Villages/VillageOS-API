using System.Text.Json;

namespace vos.MyceliumClient;

public sealed record FoundThing(string Id, string Name);

// Matched counts every Thing that matched; Things holds as many of them as were asked for.
public sealed record FoundThings(IReadOnlyList<FoundThing> Things, int Matched)
{
    public bool MoreExist => Matched > Things.Count;
}

public static class ThingSearch
{
    // The platform cannot narrow by part of a name, so every Thing the narrowing leaves is read and its
    // name compared here. Null where the platform answered something other than a list.
    public static async Task<FoundThings?> ByPartOfANameAsync(
        MyceliumClient mycelium, string part, ThingListNarrowing? narrowing = null, int? atMost = null)
    {
        var things = narrowing is null || narrowing.IsEmpty
            ? await mycelium.GetAllThingsAsync()
            : await mycelium.GetThingsAsync(narrowing);
        if (things.ValueKind != JsonValueKind.Array) return null;

        var matching = things.EnumerateArray()
            .Select(thing => new FoundThing(Text(thing, "Id"), Text(thing, "Name")))
            .Where(thing => thing.Name.Contains(part, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new FoundThings(atMost is { } most ? matching.Take(most).ToList() : matching, matching.Count);
    }

    private static string Text(JsonElement thing, string field) =>
        thing.TryGetProperty(field, out var value) ? value.GetString() ?? "" : "";
}
