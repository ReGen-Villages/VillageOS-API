namespace vos.Service.Delta.Models;

// A validated, single-rooted graph of endpoint-template things. Parentage is derived from the
// model's is relationships, not a scalar field. Construction validates what Mycelium does
// NOT (single root, at most one is parent, no duplicate name, no cycle, no unknown-template
// reference) so a misconfigured deployment fails fast at boot rather than stack-overflowing
// Mycelium's cycle-unsafe effective-property traversal.
public sealed class EndpointSeedGraph
{
    private readonly IReadOnlyDictionary<string, string> _parents;

    public IReadOnlyDictionary<string, RegisterEndpointRequest> Templates { get; }

    public RegisterEndpointRequest Root { get; }

    private EndpointSeedGraph(
        IReadOnlyDictionary<string, RegisterEndpointRequest> templates,
        RegisterEndpointRequest root,
        IReadOnlyDictionary<string, string> parents)
    {
        Templates = templates;
        Root = root;
        _parents = parents;
    }

    public string? ParentName(string templateName) =>
        _parents.TryGetValue(templateName, out var parent) ? parent : null;

    public bool ContainsTemplate(string templateName) => Templates.ContainsKey(templateName);

    // The inheritance chain for the template, nearest-first up to and including the root.
    public IReadOnlyList<RegisterEndpointRequest> Chain(string templateName)
    {
        if (!Templates.TryGetValue(templateName, out var template))
            throw new KeyNotFoundException($"Unknown template '{templateName}'.");

        var chain = new List<RegisterEndpointRequest> { template };
        var current = template.Name;
        while (_parents.TryGetValue(current, out var parent))
        {
            chain.Add(Templates[parent]);
            current = parent;
        }
        return chain;
    }

    // The union of property keys along the chain — the admissible property set for a registration.
    public ISet<string> AllowedKeys(string templateName)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in Chain(templateName))
        {
            if (template.Properties == null)
                continue;
            foreach (var key in template.Properties.Keys)
                keys.Add(key);
        }
        return keys;
    }

    // The closest-ancestor-wins seed value for the key along the chain; false if none is non-blank.
    public bool TryGetEffectiveSeedValue(string templateName, string key, out object? value)
    {
        foreach (var template in Chain(templateName))
        {
            if (template.Properties != null
                && template.Properties.TryGetValue(key, out var candidate)
                && !IsBlank(candidate))
            {
                value = candidate;
                return true;
            }
        }
        value = null;
        return false;
    }

    // A structural key with a blank value contributes admissibility but no inherited default. Values
    // arrive as CLR strings (built in-process) or JsonElement (deserialized from seed.json).
    private static bool IsBlank(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        System.Text.Json.JsonElement je =>
            je.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined
            || (je.ValueKind == System.Text.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(je.GetString())),
        _ => false
    };

    public static EndpointSeedGraph Build(EndpointSeedModel seed)
    {
        if (seed == null)
            throw new ArgumentNullException(nameof(seed));

        var things = seed.Things ?? new List<RegisterEndpointRequest>();
        if (things.Count == 0)
            throw new InvalidOperationException("Could not load a valid Endpoint seed: the template graph has no things.");

        var byName = new Dictionary<string, RegisterEndpointRequest>(StringComparer.OrdinalIgnoreCase);
        foreach (var thing in things)
        {
            if (thing == null || string.IsNullOrWhiteSpace(thing.Name))
                throw new InvalidOperationException("Endpoint seed has a thing with an empty name.");
            if (!byName.TryAdd(thing.Name, thing))
                throw new InvalidOperationException($"Duplicate template name '{thing.Name}' in endpoint seed graph.");
        }

        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in seed.Relationships ?? new List<SeedRelationship>())
        {
            if (rel == null)
                continue;
            if (string.IsNullOrWhiteSpace(rel.Subject) || string.IsNullOrWhiteSpace(rel.Predicate) || string.IsNullOrWhiteSpace(rel.Target))
                throw new InvalidOperationException("Endpoint seed has a relationship with an empty subject, predicate, or target.");
            if (!byName.ContainsKey(rel.Subject))
                throw new InvalidOperationException($"Relationship references unknown template '{rel.Subject}'.");
            if (!byName.ContainsKey(rel.Target))
                throw new InvalidOperationException($"Relationship references unknown template '{rel.Target}'.");

            if (!string.Equals(rel.Predicate, "is", StringComparison.OrdinalIgnoreCase))
                continue;

            if (parents.ContainsKey(rel.Subject))
                throw new InvalidOperationException($"Template '{rel.Subject}' declares more than one 'is' parent.");
            parents[rel.Subject] = rel.Target;
        }

        var roots = byName.Keys.Where(name => !parents.ContainsKey(name)).ToList();
        if (roots.Count == 0)
            throw new InvalidOperationException("Endpoint seed graph has no root template (every template declares 'is').");
        if (roots.Count > 1)
            throw new InvalidOperationException(
                $"Endpoint seed graph has multiple root templates: {string.Join(", ", roots)}.");

        foreach (var start in byName.Keys)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = start;
            while (parents.TryGetValue(current, out var parent))
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException(
                        $"Cycle detected in endpoint seed graph involving template '{current}'.");
                current = parent;
            }
        }

        return new EndpointSeedGraph(byName, byName[roots[0]], parents);
    }
}
