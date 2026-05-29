namespace vos.ManagedMicroservice.Delta.Models;

/// <summary>
/// Shared DTO mirroring a vos.Thing's create shape: a name plus a flat property bag.
/// Used for the entries of an <see cref="EndpointSeedModel.Things"/> list, for incoming
/// /register request bodies, and for broker thing-creation payloads. Inheritance is NOT a
/// field here — it is expressed as an 'is' relationship (see <see cref="EndpointSeedModel"/>),
/// exactly as the broker and CLI model serialization do.
/// </summary>
public class RegisterEndpointRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Properties { get; set; }
}
