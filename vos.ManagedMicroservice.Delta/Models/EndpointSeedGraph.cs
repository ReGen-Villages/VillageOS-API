namespace vos.ManagedMicroservice.Delta.Models;

/// <summary>
/// A validated, single-rooted graph of endpoint-template things, built from an
/// <see cref="EndpointSeedModel"/>. Parentage is derived from the model's <c>is</c> relationships
/// (the model-native representation of inheritance) — there is no scalar "extends" field. The broker
/// realizes the same shape as <c>is</c> relationships between the template things (Task #5468).
///
/// Construction validates what the broker does NOT: a single root, at most one <c>is</c> parent per
/// template, no duplicate template name, no cycle, and no relationship to an unknown template — so a
/// misconfigured deployment fails fast at boot rather than later stack-overflowing the broker's
/// (cycle-unsafe) effective-property traversal.
///
/// Introduced under Feature #5465 / Task #5466.
/// </summary>
public sealed class EndpointSeedGraph
{
    private readonly IReadOnlyDictionary<string, string> _parents;

    /// <summary>All template things keyed by Name (case-insensitive). Includes the root.</summary>
    public IReadOnlyDictionary<string, RegisterEndpointRequest> Templates { get; }

    /// <summary>The single template with no <c>is</c> parent.</summary>
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

    /// <summary>The parent template name for <paramref name="templateName"/>, or null if it is the root.</summary>
    public string? ParentName(string templateName) =>
        _parents.TryGetValue(templateName, out var parent) ? parent : null;

    /// <summary>
    /// Validate <paramref name="seed"/> and build the graph. Throws <see cref="InvalidOperationException"/>
    /// on: no things, an empty thing name, a duplicate name, a relationship referencing an unknown template,
    /// a template with more than one <c>is</c> parent, no root, more than one root, or a cycle.
    /// </summary>
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

        // Derive parentage from 'is' relationships; validate every relationship references known things.
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
                continue; // only 'is' contributes to the template hierarchy

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

        // Every parent-chain must terminate at the root; revisiting a name means a cycle.
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
