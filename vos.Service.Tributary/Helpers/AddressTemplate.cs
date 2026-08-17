using System.Text.RegularExpressions;

namespace vos.Service.Tributary.Helpers;

// Fills named placeholders in an endpoint's stored address from values the caller supplies, so one
// registration serves every address in a set instead of one registration per address. The
// substitution is generic: it knows the placeholder names only as text, so a tile pyramid, a point
// query at a site's coordinates, and anything later all use the same mechanism.
public static class AddressTemplate
{
    private static readonly Regex Placeholder = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    // Returns the template untouched when anything is unfilled: the caller refuses on `missing`, and
    // a half-filled address must never be mistaken for a real one. A supplied value that no
    // placeholder names is ignored rather than refused, because one caller passes a shared set of
    // values to sources whose addresses take different placeholders.
    public static string Fill(string template, IReadOnlyDictionary<string, string>? values, out List<string> missing)
    {
        var unfilled = new List<string>();
        var filled = Placeholder.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (values != null && values.TryGetValue(name, out var value))
                return Uri.EscapeDataString(value);

            unfilled.Add(name);
            return match.Value;
        });

        missing = unfilled;
        return unfilled.Count == 0 ? filled : template;
    }
}
