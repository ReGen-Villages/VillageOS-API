using System.Text.Json;
using vos.ManagedMicroservice.Delta.Models;
using vos.ManagedMicroservice.Delta.Services;

namespace vos.ManagedMicroservice.Delta.Tests.Services;

/// <summary>
/// Test-only <see cref="IEndpointSeedProvider"/> backed by a single model-seed JSON string supplied
/// at construction. <see cref="DeltaWebApplicationFactory"/> uses this to inject a per-instance seed
/// without touching <c>AppContext.BaseDirectory</c>, so factory instances no longer race on a shared
/// <c>seed.json</c> path.
///
/// Mirrors the file-loader's throw contract: malformed JSON raises
/// <see cref="InvalidOperationException"/>, and structural defects surface from
/// <see cref="EndpointSeedGraph.Build"/> (empty/duplicate name, missing/multiple root, unknown reference, cycle).
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

    public EndpointSeedGraph LoadGraph()
    {
        EndpointSeedModel? model;
        try
        {
            model = JsonSerializer.Deserialize<EndpointSeedModel>(_seedJson, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Could not load a valid Endpoint seed from in-memory JSON.", ex);
        }

        if (model == null)
            throw new InvalidOperationException("Could not load a valid Endpoint seed from in-memory JSON.");

        return EndpointSeedGraph.Build(model);
    }
}
