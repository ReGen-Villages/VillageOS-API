using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Idempotently provisions the endpoint-template catalog into Mycelium at startup. Idempotency rests on
/// find-or-create by name; a template's <c>is</c> edge is created only when the thing was newly created
/// this run. The <c>is</c> predicate is a model primitive and is never created — if missing, provisioning
/// logs and aborts (best-effort startup; Mycelium's liveness monitor covers an unusable model).
///
/// Known gap: if a thing was created on a prior run but its <c>is</c> edge failed, a later run finds the
/// thing and cannot repair the missing edge — no mycelium relationship-query API exists to detect it.
/// </summary>
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

            var createdThing = await _myceliumClient.CreateThingAsync(new RegisterEndpointRequest
            {
                Name = template.Name,
                Properties = template.Properties ?? new Dictionary<string, object>()
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

            var parentName = _graph.ParentName(template.Name);
            if (parentName == null)
                continue;

            if (!idByName.TryGetValue(parentName, out var parentId))
            {
                _logger.LogError(
                    "Parent template '{Parent}' of '{Template}' was not provisioned; skipping 'is' wiring.",
                    parentName, template.Name);
                continue;
            }

            if (await _myceliumClient.CreateRelationshipAsync(createdThing.Value.Id, isPredicate.Value.Id, parentId))
                wiredCount++;
            else
                _logger.LogError("Failed to wire 'is' relationship '{Template}' -> '{Parent}'.", template.Name, parentName);
        }

        _logger.LogInformation(
            "Endpoint template catalog provisioned: {Created} thing(s) created, {Wired} 'is' relationship(s) wired, {Total} template(s) total.",
            createdCount, wiredCount, _graph.Templates.Count);
    }
}
