using System.Text.Json;

namespace vos.Service.Tributary.Helpers;

// Looks up a property by name in a dictionary keyed by potentially-namespaced strings
// (e.g. "http.url", "resource.url"). An exact key match wins; otherwise the segment after the
// last dot is matched case-insensitively against the requested name. Several matches produce a
// conflict list so callers can surface the ambiguity instead of picking one arbitrarily — a key
// reaching Tributary twice means the model declared it twice, which the platform forbids.
// Extracted from Program.cs under Feature #5433 / Task #5436.
public static class EffectivePropertyResolver
{
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
