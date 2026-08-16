namespace vos.Service.Delta.Models;

// A Delta endpoint-template seed document — a simplified, hand-authored mirror of a VosModel
// fragment. It carries the template Things and the relationships among them. Template inheritance
// is expressed the model-native way, as is relationships in Relationships
// (never a scalar field on a thing), matching how Mycelium and CLI serialize a model. References
// are by template name; Delta resolves them to mycelium GUIDs at realization time.
// Introduced under Feature #5465 / Task #5466.
public sealed class EndpointSeedModel
{
    // Optional document name (the model name); not part of the template graph.
    public string? Name { get; set; }

    // The template things, each a name + flat property bag.
    public List<RegisterEndpointRequest> Things { get; set; } = new();

    // The vocabulary an endpoint reaches: how it authenticates, how it pages, how its body reads.
    // Held apart from Things because a kind is not an endpoint template and must not be mistaken for
    // one — it has no place in the inheritance chain and would otherwise read as a second root.
    public List<EndpointKind> Kinds { get; set; } = new();

    // Relationships among the things; is rows define the inheritance hierarchy, and a kind-role row
    // names the kind a template uses.
    public List<SeedRelationship> Relationships { get; set; } = new();
}

// One way of doing something an endpoint can point at, and what it needs from an endpoint that
// does. Requires is what makes the kind worth being a Thing: an endpoint can be judged against its
// kind before anything is called, instead of failing partway through the outbound request.
public sealed class EndpointKind
{
    public string Name { get; set; } = string.Empty;

    public List<string> Requires { get; set; } = new();
}

// The roles a template can fill by reaching a kind. Delta writes these edges and Tributary reads
// them; a role only one side spells right is an endpoint that silently authenticates as nobody.
public static class EndpointKindRoles
{
    public const string Authentication = "authenticatesBy";
    public const string Paging = "pagesBy";
    public const string ResponseBody = "readsBodyAs";

    // The property each role was written as before the kind became a Thing. A seed still carrying
    // one is refused, because provisioning it would leave the endpoint reaching nothing while
    // looking configured.
    public static readonly IReadOnlyDictionary<string, string> SupersededProperties =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authKind"] = Authentication,
            ["pagingKind"] = Paging,
            ["responseKind"] = ResponseBody,
        };

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Authentication, Paging, ResponseBody };
}

// A name-keyed relationship row in an EndpointSeedModel (mirrors a model relationship).
public sealed class SeedRelationship
{
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
