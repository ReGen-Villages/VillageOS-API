namespace vos.Service.Delta.Models;

// Shared DTO mirroring a vos.Thing's create shape: a name, a flat property bag, and the
// relationships the thing participates in. Used for the entries of an
// EndpointSeedModel.Things list, for incoming /handle and /register request bodies,
// and for mycelium thing-creation payloads.
// Inheritance is NOT a scalar field — a vos.Thing has none. It is expressed the model-native way as
// an is relationship in Relationships (a registration's is row names the
// template it descends from), exactly as Mycelium and CLI model serialization do.
public class RegisterEndpointRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Properties { get; set; }

    // Relationships the thing participates in; an is row whose subject is this thing names the
    // template it descends from. Null/empty on a registration means descend from the root Endpoint.
    public List<SeedRelationship>? Relationships { get; set; }
}
