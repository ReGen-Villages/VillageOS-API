using vos.Service.Shared;

namespace vos.Service.Delta.Models;

// A Delta endpoint-template seed document — a simplified, hand-authored mirror of a VosModel
// fragment. It carries the template Things and the relationships among them. Template inheritance
// is expressed the model-native way, as is relationships in Relationships
// (never a scalar field on a thing), matching how Mycelium and CLI serialize a model. References
// are by template name; Delta resolves them to mycelium GUIDs at realization time.
public sealed class EndpointSeedModel
{
    // Optional document name (the model name); not part of the template graph.
    public string? Name { get; set; }

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

public sealed class SeedRelationship
{
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
