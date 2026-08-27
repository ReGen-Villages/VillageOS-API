using vos.Service.Delta.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Delta.Services;

// Idempotently provisions the endpoint-template catalog into one model. Idempotency rests on
// find-or-create by name and on one read of the edges the catalog already carries: a name lookup
// alone cannot tell a finished template from one whose `is` edge failed on an earlier run, and the
// second keeps resolving to its parent's values while looking correct. The is predicate is a model
// primitive and is never created — if missing, provisioning logs and returns null, and the caller
// refuses the registration.
// A template is provisioned in three steps rather than one, because the order decides how Mycelium
// stores a key the template narrows: create the thing with the keys no ancestor declares, wire its
// is edge, then write the narrowed keys, which by then resolve as inherited and are stored as
// overrides (see NarrowedKeys).
// The narrowed keys hang off the edge rather than off the create, which is what makes a repair whole:
// a run that wires an edge some earlier run left unwritten writes them too, and a run that finds the
// edge already there writes nothing at all.
public sealed class TemplateCatalogProvisioner
{
    private readonly MyceliumClient _myceliumClient;
    private readonly ISubscriptionClient _subscriptions;
    private readonly EndpointSeedGraph _graph;
    private readonly ILogger<TemplateCatalogProvisioner> _logger;

    public TemplateCatalogProvisioner(
        MyceliumClient myceliumClient,
        ISubscriptionClient subscriptions,
        EndpointSeedGraph graph,
        ILogger<TemplateCatalogProvisioner> logger)
    {
        _myceliumClient = myceliumClient;
        _subscriptions = subscriptions;
        _graph = graph;
        _logger = logger;
    }

    public async Task<ProvisionedCatalog?> ProvisionAsync()
    {
        var isPredicate = await _myceliumClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            _logger.LogError("Cannot provision endpoint templates: mycelium model has no 'is' predicate thing.");
            return null;
        }

        if (await ReadExistingEdgesAsync() is not { } existingEdges)
            return null;

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
            var parentName = _graph.ParentName(template.Name);
            var narrowedKeys = NarrowedKeys(template, parentName);

            var existing = await _myceliumClient.FindThingByNameAsync(template.Name);
            Guid templateId;
            if (existing != null)
            {
                templateId = existing.Value.Id;
            }
            else
            {
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
                templateId = createdThing.Value.Id;
                createdCount++;
            }

            idByName[template.Name] = templateId;

            if (parentName == null)
                continue;

            if (!idByName.TryGetValue(parentName, out var parentId))
            {
                _logger.LogError(
                    "Parent template '{Parent}' of '{Template}' was not provisioned; skipping 'is' wiring.",
                    parentName, template.Name);
                continue;
            }

            if (existingEdges.Contains((templateId, isPredicate.Value.Id, parentId)))
                continue;

            if (!await _myceliumClient.CreateRelationshipAsync(templateId, isPredicate.Value.Id, parentId))
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
                if (!await _myceliumClient.SetThingPropertyAsync(templateId, key, template.Properties![key]))
                    _logger.LogError(
                        "Failed to narrow property '{Property}' on endpoint template '{Template}'; it keeps the "
                        + "value inherited from '{Parent}'.",
                        key, template.Name, parentName);
            }
        }

        // Kinds last: a role edge is written onto a template, so the template must already exist and
        // already be in its own `is` chain, or the edge lands on a Thing whose narrowed keys are still
        // unwritten and a reader resolving through it sees the parent's values.
        var kindEdges = await ProvisionKindsAsync(idByName, existingEdges);

        _logger.LogInformation(
            "Endpoint template catalog provisioned: {Created} thing(s) created, {Wired} 'is' relationship(s) wired, "
            + "{KindEdges} kind edge(s) wired, {Total} template(s) total.",
            createdCount, wiredCount, kindEdges, _graph.Templates.Count);

        return new ProvisionedCatalog(isPredicate.Value.Id, idByName);
    }

    // The edges the catalog already carries, read in one call before anything is written. A subscribe
    // is the read: it answers from the snapshot it returns, and that snapshot carries the relationships
    // a name lookup cannot.
    //
    // Refusing on a failed read, because neither reading it as "no edges" nor as "every edge" is
    // survivable: the first rewrites every edge of a finished catalog, and the second is exactly the
    // silence this class exists to end.
    private async Task<HashSet<(Guid Subject, Guid Predicate, Guid Target)>?> ReadExistingEdgesAsync()
    {
        // Every edge a template carries is incident to that template, so naming the templates reaches
        // the `is` edges and the kind edges alike without naming either predicate.
        var selector = new SubscriptionSelector
        {
            Names = [.. _graph.Templates.Values.Select(template => template.Name)],
            IncludeRelationships = true,
        };

        SubscribeResult subscribed;
        try
        {
            subscribed = await _subscriptions.SubscribeAsync(selector);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Failed to read which edges the endpoint template catalog already carries; provisioning nothing "
                + "rather than guessing at what is already wired.");
            return null;
        }

        try
        {
            return [.. subscribed.Snapshot.Relationships.Select(
                edge => (edge.SubjectId, edge.PredicateId, edge.TargetId))];
        }
        finally
        {
            try
            {
                await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId);
            }
            catch (Exception exception)
            {
                // The read already succeeded; failing to release the subscription must not lose it.
                _logger.LogWarning(exception, "Failed to release the catalog-read subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }

    // Mints each kind and each role predicate find-or-create, then relates every template to the kind
    // it declares. A kind that cannot be minted takes its edges with it: an endpoint reaching nothing
    // is refused by Tributary, where an endpoint reaching a Thing that does not exist is not.
    private async Task<int> ProvisionKindsAsync(
        Dictionary<string, Guid> templateIds,
        HashSet<(Guid Subject, Guid Predicate, Guid Target)> existingEdges)
    {
        var wired = 0;
        var kindIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var roleIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in _graph.Kinds.Values)
        {
            var id = await FindOrCreateAsync(kind.Name, RequirementProperties(kind));
            if (id == null)
            {
                _logger.LogError(
                    "Failed to provision endpoint kind '{Kind}'; templates naming it are left unrelated.", kind.Name);
                continue;
            }
            kindIds[kind.Name] = id.Value;
        }

        foreach (var (template, role, kindName) in _graph.KindEdges)
        {
            if (!templateIds.TryGetValue(template, out var subjectId)
                || !kindIds.TryGetValue(kindName, out var targetId))
                continue;

            if (!roleIds.TryGetValue(role, out var roleId))
            {
                var minted = await FindOrCreateAsync(role, properties: null);
                if (minted == null)
                {
                    _logger.LogError("Failed to provision role predicate '{Role}'; no template can use it.", role);
                    continue;
                }
                roleIds[role] = roleId = minted.Value;
            }

            if (existingEdges.Contains((subjectId, roleId, targetId)))
                continue;

            if (await _myceliumClient.CreateRelationshipAsync(subjectId, roleId, targetId))
                wired++;
            else
                _logger.LogError(
                    "Failed to relate '{Template}' to kind '{Kind}' through '{Role}'; the endpoint will be refused "
                    + "as reaching no {Role} kind.", template, kindName, role, role);
        }

        return wired;
    }

    // A requirement is declared the way a template declares a structural key — by name, with no value.
    // The kind says what an endpoint must supply; only the endpoint can say what it supplies.
    private static Dictionary<string, object> RequirementProperties(EndpointKind kind) =>
        kind.Requires.ToDictionary(required => required, _ => (object)string.Empty);

    private async Task<Guid?> FindOrCreateAsync(string name, Dictionary<string, object>? properties)
    {
        var existing = await _myceliumClient.FindThingByNameAsync(name);
        if (existing != null)
            return existing.Value.Id;

        var created = await _myceliumClient.CreateThingAsync(new RegisterEndpointRequest
        {
            Name = name,
            Properties = properties,
        });
        return created?.Id;
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
