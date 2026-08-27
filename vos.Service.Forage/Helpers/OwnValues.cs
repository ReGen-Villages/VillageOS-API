using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

// One Thing's own property values, as parameters a source's address may name.
//
// Every value goes to every call the Thing addresses, and a source's address takes only the
// placeholders it names — the fetcher ignores a value no placeholder uses. So one set of values
// serves a source wanting coordinates, one wanting a division code, and one wanting neither, with
// no per-source arrangement here.
public static class OwnValues
{
    // Own properties only. A site's coordinates are its own; a value inherited from an archetype is
    // a default for a kind of site, and calling a provider with a default location would return a
    // confident reading about somewhere else.
    public static IReadOnlyDictionary<string, string> Of(SnapshotDocument snapshot, Guid thingId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var thing = snapshot.Things.FirstOrDefault(candidate => candidate.Id == thingId);
        if (thing == null) return values;

        foreach (var (name, property) in thing.Properties)
        {
            // A double-underscored name is a mark a reader finds the Thing by, not a value about it.
            if (name.StartsWith("__", StringComparison.Ordinal)) continue;
            if (AsText(property.Value) is { } text && !string.IsNullOrWhiteSpace(text))
                values[name] = text;
        }

        return values;
    }

    // A placeholder is filled with text, so a number arrives as the digits the source's own JSON
    // held rather than reformatted through this process's culture.
    private static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };
}
