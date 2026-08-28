using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Helpers;

// The values one Thing states about itself, as parameters a source's address may name.
//
// Every value goes to every call the Thing addresses, and a source's address takes only the
// placeholders it names — the fetcher ignores a value no placeholder uses. So one set of values
// serves a source wanting coordinates, one wanting a division code, and one wanting neither, with
// no per-source arrangement here.
public static class StatedValues
{
    // What the Thing states, never what the `is` chain resolves. A site's coordinates are its own; a
    // value inherited from an archetype is a default for a kind of site, and calling a provider with a
    // default location would return a confident reading about somewhere else. A submitted site states
    // its coordinates as overrides, because the archetype declares those names, so reading own
    // properties alone addressed every submitted site with nothing at all (#6805).
    public static IReadOnlyDictionary<string, string> Of(SnapshotDocument snapshot, Guid thingId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var thing = snapshot.Things.FirstOrDefault(candidate => candidate.Id == thingId);
        if (thing == null) return values;

        foreach (var (name, property) in thing.ValuesStated())
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
