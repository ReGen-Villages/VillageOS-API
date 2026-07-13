using System.Text.Json;

namespace vos.ManagedMicroservice.Tributary.Helpers;

// Looks up a property by name in a dictionary keyed by potentially-namespaced strings
// (e.g. "http.url", "resource.url"). Exact match wins; otherwise, the
// suffix after the last . is matched case-insensitively against the requested
// name. Multiple suffix matches produce a conflict list so callers can surface the
// ambiguity instead of picking one arbitrarily.
// Extracted from Program.cs under Feature #5433 / Task #5436.
public static class EffectivePropertyResolver
{
    // Try to resolve name in properties.
    //   Exact key match → returns true, value set.
    //   Single key whose suffix (after the last .) matches name case-insensitively → returns true.
    //   Multiple suffix matches → returns false, conflicts lists every matching key.
    //   No match → returns false, both out-params at their defaults.
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
