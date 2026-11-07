namespace vos.Service.Delta.Models;

// Mirrors a vos.Thing's create shape: a name, a flat property bag, and the relationships the thing participates
// in. Used for incoming /handle and /register request bodies and for mycelium thing-creation payloads.
// Inheritance is NOT a scalar field — a vos.Thing has none. A registration's is row names the template it
// descends from, exactly as Mycelium and CLI model serialization do.
public class RegisterEndpointRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Properties { get; set; }

    // No is row means the registration descends from the template the endpoint role binds.
    public List<RequestedRelationship>? Relationships { get; set; }
}

public sealed class RequestedRelationship
{
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
