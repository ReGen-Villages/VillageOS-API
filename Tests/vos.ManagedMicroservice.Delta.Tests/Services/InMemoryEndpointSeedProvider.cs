using System.Text.Json;
using vos.ManagedMicroservice.Delta.Models;
using vos.ManagedMicroservice.Delta.Services;

namespace vos.ManagedMicroservice.Delta.Tests.Services;

/// <summary>
/// Test-only <see cref="IEndpointSeedProvider"/> backed by a JSON string supplied at
/// construction. <see cref="DeltaWebApplicationFactory"/> uses this to inject a per-instance
/// seed without touching <c>AppContext.BaseDirectory</c>, so factory instances no longer
/// race on a shared <c>seed.json</c> path.
///
/// Mirrors the file-loader's throw contract: <see cref="LoadSeed"/> raises
/// <see cref="InvalidOperationException"/> when the JSON cannot be parsed into a
/// <see cref="RegisterEndpointRequest"/> with a non-empty <see cref="RegisterEndpointRequest.Name"/>.
/// </summary>
public sealed class InMemoryEndpointSeedProvider : IEndpointSeedProvider
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _seedJson;

    public InMemoryEndpointSeedProvider(string seedJson)
    {
        _seedJson = seedJson;
    }

    public RegisterEndpointRequest LoadSeed()
    {
        RegisterEndpointRequest? seed;
        try
        {
            seed = JsonSerializer.Deserialize<RegisterEndpointRequest>(_seedJson, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Could not load a valid Endpoint seed from in-memory JSON.", ex);
        }

        if (seed == null || string.IsNullOrWhiteSpace(seed.Name))
            throw new InvalidOperationException("Could not load a valid Endpoint seed from in-memory JSON.");

        return seed;
    }
}
