using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Tributary.Models;

public class EndpointCallRequest
{
    [JsonPropertyName("endpointName")]
    public string EndpointName { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public JsonElement Body { get; set; }

    // Reshapes this call only, in place of whatever the endpoint has in effect. It is never written
    // back to the endpoint Thing, so it cannot change what a later caller of the same source gets.
    [JsonPropertyName("responseTransform")]
    public string? ResponseTransform { get; set; }

    // Values for the named placeholders in the endpoint's stored address, so one registration serves
    // every address in a set. Like the reshape override, they belong to this call alone.
    [JsonPropertyName("addressParameters")]
    public Dictionary<string, string>? AddressParameters { get; set; }

    // The Thing this call's readings are about. Supplied when one registration serves many subjects:
    // the address already varies per call, and without this the reading's destination would still be
    // whatever name the registration's expression carries, so every subject's values would land on
    // one Thing.
    //
    // An id rather than a name, because the caller already holds the Thing it asked about. A name
    // would have to be resolved, a name matching two Things is refused as ambiguous and reads back as
    // absent, and the ingest creates what it cannot find — so a name would answer a duplicate by
    // quietly minting a third.
    [JsonPropertyName("subjectId")]
    public Guid? SubjectId { get; set; }
}
