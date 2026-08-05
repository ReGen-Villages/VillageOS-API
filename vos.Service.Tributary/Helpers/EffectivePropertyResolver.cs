using System.Text.Json;

namespace vos.Service.Tributary.Helpers;

// Looks up a property by name in a dictionary keyed by potentially-namespaced strings
// (e.g. "http.url", "resource.url"). Exact match wins; otherwise, the
// suffix after the last . is matched case-insensitively against the requested
// name. Suffix matches whose qualifier paths all lie on one inheritance chain are
// shadowing, not ambiguity — the closest ancestor (shortest path) wins (Bug #6152).
// Matches from divergent branches produce a conflict list so callers can surface the
// ambiguity instead of picking one arbitrarily.
// Extracted from Program.cs under Feature #5433 / Task #5436.
public static class EffectivePropertyResolver
{
    // Try to resolve name in properties.
    //   Exact key match → returns true, value set.
    //   Single key whose suffix (after the last .) matches name case-insensitively → returns true.
    //   Multiple suffix matches on one prefix-chain (e.g. "A.x" and "A.B.x") → returns true
    //     with the shortest path's value — the closest declaration on the `is` chain.
    //   Suffix matches from divergent branches → returns false, conflicts lists every matching key.
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
        {
            var ordered = matches.OrderBy(m => QualifierPath(m).Length).ToList();
            if (IsSinglePrefixChain(ordered))
            {
                value = properties[ordered[0]];
                return true;
            }
            conflicts = matches;
        }

        value = default;
        return false;
    }

    // The segments before the property name: "A.B.x" → ["A","B"]. An unqualified key has none.
    private static string[] QualifierPath(string key)
    {
        var segments = key.Split('.');
        return segments[..^1];
    }

    // True when every shorter qualifier path is a segment-prefix of every longer one, i.e. all
    // matches sit on a single inheritance chain. Segment-wise comparison — "Esri" is a string
    // prefix of "EsriEndpoint" but a different type, so it must not chain.
    private static bool IsSinglePrefixChain(List<string> orderedByDepth)
    {
        for (var i = 1; i < orderedByDepth.Count; i++)
        {
            var shorter = QualifierPath(orderedByDepth[i - 1]);
            var longer = QualifierPath(orderedByDepth[i]);
            if (shorter.Length >= longer.Length)
                return false;
            for (var s = 0; s < shorter.Length; s++)
            {
                if (!string.Equals(shorter[s], longer[s], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }
}
