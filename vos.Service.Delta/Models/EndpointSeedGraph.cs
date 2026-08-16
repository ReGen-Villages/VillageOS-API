namespace vos.Service.Delta.Models;

// A validated, single-rooted graph of endpoint-template things. Parentage is derived from the
// model's is relationships, not a scalar field. Construction validates what Mycelium does
// NOT (single root, at most one is parent, no duplicate name, no cycle, no unknown-template
// reference) so a misconfigured deployment fails fast at boot rather than stack-overflowing
// Mycelium's cycle-unsafe effective-property traversal.
public sealed class EndpointSeedGraph
{
    private readonly IReadOnlyDictionary<string, string> _parents;

    // (template, role) -> kind name. Only what a template declares itself; the chain is walked on read.
    private readonly IReadOnlyDictionary<(string Template, string Role), string> _kindEdges;

    public IReadOnlyDictionary<string, RegisterEndpointRequest> Templates { get; }

    public IReadOnlyDictionary<string, EndpointKind> Kinds { get; }

    // The kind edges as declared, for provisioning. Resolution walks the chain; provisioning writes
    // only what a template declares itself, because an inherited edge is reached through `is`.
    public IEnumerable<(string Template, string Role, string Kind)> KindEdges =>
        _kindEdges.Select(entry => (entry.Key.Template, entry.Key.Role, entry.Value));

    public RegisterEndpointRequest Root { get; }

    private EndpointSeedGraph(
        IReadOnlyDictionary<string, RegisterEndpointRequest> templates,
        RegisterEndpointRequest root,
        IReadOnlyDictionary<string, string> parents,
        IReadOnlyDictionary<string, EndpointKind> kinds,
        IReadOnlyDictionary<(string, string), string> kindEdges)
    {
        Templates = templates;
        Root = root;
        _parents = parents;
        Kinds = kinds;
        _kindEdges = kindEdges;
    }

    // The kind this template reaches for the role, nearest-first up the is chain — the same
    // closest-ancestor-wins rule a narrowed property follows, so a kind and a key are inherited alike.
    public EndpointKind? ResolveKind(string templateName, string role)
    {
        foreach (var template in Chain(templateName))
            if (_kindEdges.TryGetValue((template.Name, role), out var kindName))
                return Kinds[kindName];
        return null;
    }

    // What the template's kind for this role requires and the template's chain does not supply.
    // Empty when the kind is satisfied, and when there is no kind to satisfy.
    public IReadOnlyList<string> MissingRequirements(string templateName, string role)
    {
        var kind = ResolveKind(templateName, role);
        if (kind == null)
            return Array.Empty<string>();

        var available = AllowedKeys(templateName);
        return kind.Requires
            .Where(required => !available.Contains(required)
                || !TryGetEffectiveSeedValue(templateName, required, out _))
            .ToList();
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

        var kinds = new Dictionary<string, EndpointKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in seed.Kinds ?? new List<EndpointKind>())
        {
            if (kind == null || string.IsNullOrWhiteSpace(kind.Name))
                throw new InvalidOperationException("Endpoint seed has a kind with an empty name.");
            if (byName.ContainsKey(kind.Name))
                throw new InvalidOperationException(
                    $"'{kind.Name}' is both an endpoint template and a kind; a kind is what a template reaches, not a template.");
            if (!kinds.TryAdd(kind.Name, kind))
                throw new InvalidOperationException($"Duplicate kind name '{kind.Name}' in endpoint seed graph.");
        }

        foreach (var template in byName.Values)
            foreach (var (superseded, role) in EndpointKindRoles.SupersededProperties)
                if (template.Properties?.ContainsKey(superseded) == true)
                    throw new InvalidOperationException(
                        $"Template '{template.Name}' sets '{superseded}'. A kind is a Thing an endpoint reaches, "
                        + $"not a word it carries: remove the property and relate the template to a kind with "
                        + $"'{role}' instead.");

        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kindEdges = new Dictionary<(string, string), string>();
        foreach (var rel in seed.Relationships ?? new List<SeedRelationship>())
        {
            if (rel == null)
                continue;
            if (string.IsNullOrWhiteSpace(rel.Subject) || string.IsNullOrWhiteSpace(rel.Predicate) || string.IsNullOrWhiteSpace(rel.Target))
                throw new InvalidOperationException("Endpoint seed has a relationship with an empty subject, predicate, or target.");
            if (!byName.ContainsKey(rel.Subject))
                throw new InvalidOperationException($"Relationship references unknown template '{rel.Subject}'.");

            if (EndpointKindRoles.All.Contains(rel.Predicate))
            {
                if (!kinds.ContainsKey(rel.Target))
                    throw new InvalidOperationException(
                        $"Template '{rel.Subject}' relates to unknown kind '{rel.Target}' through '{rel.Predicate}'.");
                if (!kindEdges.TryAdd((rel.Subject, rel.Predicate), rel.Target))
                    throw new InvalidOperationException(
                        $"Template '{rel.Subject}' declares more than one '{rel.Predicate}' kind.");
                continue;
            }

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

        return new EndpointSeedGraph(byName, byName[roots[0]], parents, kinds, kindEdges);
    }
}
