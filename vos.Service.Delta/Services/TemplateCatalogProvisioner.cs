using vos.Service.Delta.Models;

namespace vos.Service.Delta.Services;

// Idempotently provisions the endpoint-template catalog into Mycelium at startup. Idempotency rests on
// find-or-create by name; a template's is edge is created only when the thing was newly created
// this run. The is predicate is a model primitive and is never created — if missing, provisioning
// logs and aborts (best-effort startup; Mycelium's liveness monitor covers an unusable model).
// A template is provisioned in three steps rather than one, because the order decides how Mycelium
// stores a key the template narrows: create the thing with the keys no ancestor declares, wire its
// is edge, then write the narrowed keys, which by then resolve as inherited and are stored as
// overrides (see NarrowedKeys).
// Known gap: if a thing was created on a prior run but its is edge failed, a later run finds the
// thing and cannot repair the missing edge — no mycelium relationship-query API exists to detect it.
// The same gap leaves that run's narrowed keys unwritten, so the template silently keeps the
// parent's values.
public sealed class TemplateCatalogProvisioner
{
    private readonly MyceliumClient _myceliumClient;
    private readonly EndpointSeedGraph _graph;
    private readonly ILogger<TemplateCatalogProvisioner> _logger;

    public TemplateCatalogProvisioner(
        MyceliumClient myceliumClient,
        EndpointSeedGraph graph,
        ILogger<TemplateCatalogProvisioner> logger)
    {
        _myceliumClient = myceliumClient;
        _graph = graph;
        _logger = logger;
    }

    public async Task ProvisionAsync()
    {
        var isPredicate = await _myceliumClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            _logger.LogError("Cannot provision endpoint templates: mycelium model has no 'is' predicate thing.");
            return;
        }

        // Ascending chain length is a valid topological order: a template's chain is strictly longer
        // than its parent's, so parents are always provisioned before their children.
        var templatesRootFirst = _graph.Templates.Values
            .OrderBy(t => _graph.Chain(t.Name).Count)
            .ToList();

        var idByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var createdCount = 0;
        var wiredCount = 0;

        foreach (var template in templatesRootFirst)
        {
            var existing = await _myceliumClient.FindThingByNameAsync(template.Name);
            if (existing != null)
            {
                idByName[template.Name] = existing.Value.Id;
                continue;
            }

            var parentName = _graph.ParentName(template.Name);
            var narrowedKeys = NarrowedKeys(template, parentName);

            var createdThing = await _myceliumClient.CreateThingAsync(new RegisterEndpointRequest
            {
                Name = template.Name,
                Properties = PropertiesOtherThan(template, narrowedKeys)
            });
            if (createdThing == null)
            {
                _logger.LogError(
                    "Failed to create endpoint template thing '{Template}'; its descendants cannot be wired this run.",
                    template.Name);
                continue;
            }

            idByName[template.Name] = createdThing.Value.Id;
            createdCount++;

            if (parentName == null)
                continue;

            if (!idByName.TryGetValue(parentName, out var parentId))
            {
                _logger.LogError(
                    "Parent template '{Parent}' of '{Template}' was not provisioned; skipping 'is' wiring.",
                    parentName, template.Name);
                continue;
            }

            if (!await _myceliumClient.CreateRelationshipAsync(createdThing.Value.Id, isPredicate.Value.Id, parentId))
            {
                _logger.LogError(
                    "Failed to wire 'is' relationship '{Template}' -> '{Parent}'; leaving its narrowed properties "
                    + "unwritten, since without the edge they would become own properties shadowing the parent's.",
                    template.Name, parentName);
                continue;
            }
            wiredCount++;

            foreach (var key in narrowedKeys)
            {
                if (!await _myceliumClient.SetThingPropertyAsync(createdThing.Value.Id, key, template.Properties![key]))
                    _logger.LogError(
                        "Failed to narrow property '{Property}' on endpoint template '{Template}'; it keeps the "
                        + "value inherited from '{Parent}'.",
                        key, template.Name, parentName);
            }
        }

        _logger.LogInformation(
            "Endpoint template catalog provisioned: {Created} thing(s) created, {Wired} 'is' relationship(s) wired, {Total} template(s) total.",
            createdCount, wiredCount, _graph.Templates.Count);
    }

    // The template's keys that an ancestor already declares. Mycelium turns a write to an inherited
    // name into an override, but only once the `is` edge exists — carried on the create instead, the
    // key becomes an own property shadowing a name the template also inherits, which the platform
    // forbids and which makes the key surface twice in every descendant's resolved view.
    private HashSet<string> NarrowedKeys(RegisterEndpointRequest template, string? parentName)
    {
        var narrowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (parentName == null || template.Properties == null)
            return narrowed;

        var inherited = _graph.AllowedKeys(parentName);
        foreach (var key in template.Properties.Keys)
            if (inherited.Contains(key))
                narrowed.Add(key);
        return narrowed;
    }

    private static Dictionary<string, object> PropertiesOtherThan(RegisterEndpointRequest template, HashSet<string> excluded) =>
        (template.Properties ?? new Dictionary<string, object>())
            .Where(entry => !excluded.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
}
