using System.Text.RegularExpressions;

namespace vos.Service.Tributary.Helpers;

// Fills named placeholders in an endpoint's stored address from values the caller supplies, so one
// registration serves every address in a set instead of one registration per address. The
// substitution is generic: it knows the placeholder names only as text, so a tile pyramid, a point
// query at a site's coordinates, and anything later all use the same mechanism.
public static class AddressTemplate
{
    private static readonly Regex Placeholder = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private static readonly IReadOnlyList<string> NothingUnfilled = Array.Empty<string>();

    // Returns the template untouched when anything is unfilled: the caller refuses on `missing`, and
    // a half-filled address must never be mistaken for a real one. A supplied value that no
    // placeholder names is ignored rather than refused, because one caller passes a shared set of
    // values to sources whose addresses take different placeholders.
    //
    // Names match case-insensitively, and the lookup is built here rather than assumed of the
    // caller: the values arrive deserialized from a request body, which gives an ordinal dictionary
    // whose behaviour would silently differ from every other property map in this service.
    public static string Fill(
        string template, IReadOnlyDictionary<string, string>? values, out IReadOnlyList<string> missing)
    {
        missing = NothingUnfilled;
        if (!template.Contains('{'))
            return template;

        var supplied = values is { Count: > 0 }
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : null;

        List<string>? unfilled = null;
        var filled = Placeholder.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (supplied != null && supplied.TryGetValue(name, out var value))
                return Uri.EscapeDataString(value);

            (unfilled ??= new List<string>()).Add(name);
            return match.Value;
        });

        if (unfilled == null)
            return filled;

        missing = unfilled;
        return template;
    }
}
