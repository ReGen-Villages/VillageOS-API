namespace vos.ManagedMicroservice.Delta.Models;

/// <summary>
/// Shared DTO mirroring a vos.Thing's create shape: a name, a flat property bag, and the
/// relationships the thing participates in. Used for the entries of an
/// <see cref="EndpointSeedModel.Things"/> list, for incoming /handle and /register request bodies,
/// and for broker thing-creation payloads.
///
/// Inheritance is NOT a scalar field — a vos.Thing has none. It is expressed the model-native way as
/// an <c>is</c> relationship in <see cref="Relationships"/> (a registration's <c>is</c> row names the
/// template it descends from), exactly as the broker and CLI model serialization do.
/// </summary>
public class RegisterEndpointRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Relationships the thing participates in; an <c>is</c> row whose subject is this thing names the
    /// template it descends from. Null/empty on a registration means descend from the root Endpoint.
    /// </summary>
    public List<SeedRelationship>? Relationships { get; set; }
}
