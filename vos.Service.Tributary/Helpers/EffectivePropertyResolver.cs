using System.Text.Json;

namespace vos.Service.Tributary.Helpers;

// Looks up a property by name in a dictionary keyed by potentially-namespaced strings
// (e.g. "http.url", "resource.url"). An exact key match wins; otherwise the segment after the
// last dot is matched case-insensitively against the requested name.
// Mycelium reports a key that several templates on one `is` chain declare once per declaring
// template, qualified by the path from the endpoint Thing — "EsriEndpoint.requestContentType"
// alongside "EsriEndpoint.Endpoint.requestContentType". Those are one key shadowed along a
// chain, not two candidates, so the shortest path — the closest declaration — wins. Matches on
// paths that diverge produce a conflict list so callers can surface the ambiguity instead of
// picking one arbitrarily.
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
        {
            var nearestFirst = matches
                .Select(key => (key, qualifierPath: QualifierPath(key)))
                .OrderBy(match => match.qualifierPath.Length)
                .ToList();

            if (LieOnOneChain(nearestFirst.Select(match => match.qualifierPath).ToList()))
            {
                value = properties[nearestFirst[0].key];
                return true;
            }

            conflicts = matches;
        }

        value = default;
        return false;
    }

    private static string[] QualifierPath(string key) => key.Split('.')[..^1];

    private static bool LieOnOneChain(List<string[]> qualifierPathsNearestFirst)
    {
        for (var index = 1; index < qualifierPathsNearestFirst.Count; index++)
        {
            var nearer = qualifierPathsNearestFirst[index - 1];
            var farther = qualifierPathsNearestFirst[index];
            if (nearer.Length >= farther.Length)
                return false;

            // Compared segment by segment: "Esri" is a string prefix of "EsriEndpoint" but names a
            // different template, so the two must not be read as one chain.
            for (var segment = 0; segment < nearer.Length; segment++)
            {
                if (!string.Equals(nearer[segment], farther[segment], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }
}
