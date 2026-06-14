using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Idempotently provisions the endpoint-template catalog into Mycelium at startup (Task #5468):
/// every template in the seed graph is created as a thing (name + seed properties) and wired to its
/// parent with an <c>is</c> relationship, so a registration never has to create a template lazily on
/// first use.
///
/// Idempotency: each template thing is find-or-created by name, and a template's <c>is</c> edge to
/// its parent is created only when the template thing was newly created on this run. On a restart
/// where the things already exist, nothing is created or rewired. The <c>is</c> predicate is a mycelium
/// model primitive and is never created — if it is missing, provisioning logs and aborts (best-effort
/// startup, mirroring the service's courtesy registration; Mycelium's liveness monitor covers a
/// service that cannot reach a usable model).
///
/// Caveat: if a thing was created on a prior run but its <c>is</c> edge failed, a later run finds the
/// thing and will not repair the missing edge — there is no mycelium relationship-query API to detect
/// it. Acceptable pre-release under the single-active-model assumption; the failure is logged loudly.
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

    /// <summary>
    /// Create every template thing (root-first) and wire each newly-created child to its parent via
    /// <c>is</c>. Best-effort: a mycelium failure on one template is logged and does not throw.
    /// </summary>
    public async Task ProvisionAsync()
    {
        var isPredicate = await _myceliumClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            _logger.LogError("Cannot provision endpoint templates: mycelium model has no 'is' predicate thing.");
            return;
        }

        // Parents before children: a template's chain is strictly longer than its parent's, so
        // ascending chain length is a valid topological order (same-depth siblings are independent).
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
                // Already provisioned (assume wired on the run that created it — see class caveat).
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
                continue; // root: no `is` relationship

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
