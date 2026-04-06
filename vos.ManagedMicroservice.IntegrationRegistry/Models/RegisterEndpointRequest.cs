namespace vos.ManagedMicroservice.IntegrationRegistry.Models;

/// <summary>
/// Shared DTO for endpoint seed loading (seed.json), incoming /register requests,
/// and broker thing creation payloads.
/// </summary>
public class RegisterEndpointRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Properties { get; set; }
}
