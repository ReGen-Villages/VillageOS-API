using System.Text.Json;
using System.Text.Json.Serialization;

namespace vos.Service.Tributary.Models;

public class EndpointCallRequest
{
    [JsonPropertyName("endpointName")]
    public string EndpointName { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public JsonElement Body { get; set; }

    [JsonPropertyName("responseTransform")]
    public string? ResponseTransform { get; set; }
}
