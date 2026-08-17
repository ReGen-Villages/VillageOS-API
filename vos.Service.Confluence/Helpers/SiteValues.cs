using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Confluence.Helpers;

// The site's own property values, as the parameters a source's address may name.
//
// Every value goes to every source, and a source's address takes only the placeholders it names —
// the fetcher ignores a value no placeholder uses. So one site's values serve a source wanting
// coordinates, one wanting elevation, and one wanting nothing, with no per-source arrangement here.
public static class SiteValues
{
    // Own properties only. A site's coordinates are its own; a value inherited from an archetype is
    // a default for a kind of site, and calling a provider with a default location would return a
    // confident reading about somewhere else.
    public static IReadOnlyDictionary<string, string> Of(SnapshotDocument snapshot, Guid siteId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var site = snapshot.Things.FirstOrDefault(thing => thing.Id == siteId);
        if (site == null) return values;

        foreach (var (name, property) in site.Properties)
        {
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
