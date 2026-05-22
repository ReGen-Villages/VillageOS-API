using System.Text.Json;

namespace vos.ManagedMicroservice.Tributary.Helpers;

/// <summary>
/// Looks up a property by name in a dictionary keyed by potentially-namespaced strings
/// (e.g. <c>"http.url"</c>, <c>"resource.url"</c>). Exact match wins; otherwise, the
/// suffix after the last <c>.</c> is matched case-insensitively against the requested
/// name. Multiple suffix matches produce a conflict list so callers can surface the
/// ambiguity instead of picking one arbitrarily.
///
/// Extracted from <c>Program.cs</c> under Feature #5433 / Task #5436.
/// </summary>
public static class EffectivePropertyResolver
{
    /// <summary>
    /// Try to resolve <paramref name="name"/> in <paramref name="properties"/>.
    /// <list type="number">
    ///   <item>Exact key match → returns true, <paramref name="value"/> set.</item>
    ///   <item>Single key whose suffix (after the last <c>.</c>) matches <paramref name="name"/> case-insensitively → returns true.</item>
    ///   <item>Multiple suffix matches → returns false, <paramref name="conflicts"/> lists every matching key.</item>
    ///   <item>No match → returns false, both out-params at their defaults.</item>
    /// </list>
    /// </summary>
    public static bool TryGetEffectiveProperty(
        Dictionary<string, JsonElement> properties,
        string name,
        out JsonElement value,
        out List<string>? conflicts)
    {
        conflicts = null;
        if (properties.TryGetValue(name, out value))
            return true;

        var matches = new List<string>();
        foreach (var entry in properties)
        {
            var key = entry.Key;
            var lastDot = key.LastIndexOf('.');
            var suffix = lastDot >= 0 ? key[(lastDot + 1)..] : key;
            if (string.Equals(suffix, name, StringComparison.OrdinalIgnoreCase))
                matches.Add(key);
        }

        if (matches.Count == 1)
        {
            value = properties[matches[0]];
            return true;
        }

        if (matches.Count > 1)
            conflicts = matches;

        value = default;
        return false;
    }
}
