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

    // The Thing this call's readings are about, for a registration serving many subjects. The
    // address varies per call but a reshape expression does not, so without this every subject's
    // values land on the one Thing that expression names. An id rather than a name because a name
    // matching two Things reads back as absent, and this ingest creates what it cannot find.
    [JsonPropertyName("subjectId")]
    public Guid? SubjectId { get; set; }
}
