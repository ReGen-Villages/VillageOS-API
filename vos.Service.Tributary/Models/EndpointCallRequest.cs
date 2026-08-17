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
}
