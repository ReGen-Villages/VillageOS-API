using vos.Service.Shared.Subscriptions;

namespace vos.Service.Delta.Models;

public sealed record LocatedTemplate(Guid Id, string Name, Guid IsPredicateId);

public sealed record TemplateRefusal(int StatusCode, string Reason);

// The keys a template carries are not read here, because the broker resolves them up the `is` chain.
public static class EndpointTemplateTree
{
    // The standard version 5 identifier of the word `endpoint` under the platform's role namespace, so every seed
    // declares the same role whatever it calls the template the role binds.
    public static readonly Guid EndpointRoleId = new("d5902341-bbcb-5917-9456-7109e76ec2b8");

    private const string Is = "is";
    private const string Binds = "binds";

    public static SubscriptionSelector Selector(string? templateName) => new()
    {
        Ids = [EndpointRoleId],
        Names = templateName is null ? [Is, Binds] : [templateName, Is, Binds],
        Traverse = [new TraverseRule { Predicate = Binds, Direction = "outgoing", Depth = 1 }],
        IncludeIsAncestors = true,
        IncludeRelationships = true,
        Relationships =
        [
            new RelationshipRule { Predicate = Is, Direction = "outgoing" },
            new RelationshipRule { Predicate = Binds, Direction = "outgoing" },
        ],
    };

    // A null name is a registration that names no template, which descends from the root itself.
    public static (LocatedTemplate? Template, TemplateRefusal? Refusal) Locate(SnapshotDocument snapshot, string? templateName)
    {
        var thingsById = snapshot.Things.ToDictionary(thing => thing.Id);

        var isPredicates = Named(snapshot, Is).ToList();
        if (isPredicates.Count != 1)
            return Refused(500, $"The model holds {isPredicates.Count} Things named '{Is}'; registering needs exactly one.");
        var isPredicate = isPredicates[0].Id;

        var bindsPredicates = Named(snapshot, Binds).Select(binds => binds.Id).ToHashSet();
        var bound = snapshot.Relationships
            .Where(edge => edge.SubjectId == EndpointRoleId && bindsPredicates.Contains(edge.PredicateId))
            .Select(edge => edge.TargetId).Distinct()
            .Select(id => thingsById.GetValueOrDefault(id)).OfType<SnapshotThing>()
            .ToList();
        if (bound.Count == 0)
            return Refused(409, "This model binds nothing to the endpoint role, so it holds no endpoint template to "
                + "register against. Load a seed that declares the endpoint templates.");
        if (bound.Count > 1)
            return Refused(409, $"The endpoint role binds {bound.Count} Things: {Listed(bound)}. A role binds one Thing.");
        var root = bound[0];

        if (templateName is null)
            return (new LocatedTemplate(root.Id, root.Name ?? "", isPredicate), null);

        var parents = snapshot.Relationships
            .Where(edge => edge.PredicateId == isPredicate)
            .ToLookup(edge => edge.SubjectId, edge => edge.TargetId);
        var candidates = Named(snapshot, templateName)
            .Where(thing => thing.IsArchetype && DescendsFrom(thing.Id, root.Id, parents))
            .ToList();

        return candidates.Count switch
        {
            0 => Refused(400, $"Unknown endpoint template '{templateName}'; it does not descend from the root '{root.Name}'."),
            1 => (new LocatedTemplate(candidates[0].Id, candidates[0].Name ?? "", isPredicate), null),
            _ => Refused(409, $"{candidates.Count} endpoint templates beneath '{root.Name}' are named '{templateName}': "
                + $"{Listed(candidates)}. Rename all but one."),
        };
    }

    private static bool DescendsFrom(Guid start, Guid root, ILookup<Guid, Guid> parents)
    {
        var seen = new HashSet<Guid>();
        var waiting = new Stack<Guid>([start]);
        while (waiting.TryPop(out var current))
        {
            if (current == root) return true;
            if (!seen.Add(current)) continue;
            foreach (var parent in parents[current])
                waiting.Push(parent);
        }
        return false;
    }

    private static IEnumerable<SnapshotThing> Named(SnapshotDocument snapshot, string name) =>
        snapshot.Things.Where(thing => string.Equals(thing.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Listed(IEnumerable<SnapshotThing> things) =>
        string.Join(", ", things.Select(thing => $"'{thing.Name}' ({thing.Id})"));

    private static (LocatedTemplate?, TemplateRefusal?) Refused(int statusCode, string reason) =>
        (null, new TemplateRefusal(statusCode, reason));
}
