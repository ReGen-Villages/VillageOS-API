using System.Text.Json;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;

namespace vos.Service.Delta.Tests.Services;

// Test-only IEndpointSeedProvider backed by a single model-seed JSON string supplied
// at construction. DeltaWebApplicationFactory uses this to inject a per-instance seed
// without touching AppContext.BaseDirectory, so factory instances no longer race on a shared
// seed.json path.
// Mirrors the file-loader's throw contract: malformed JSON raises
// InvalidOperationException, and structural defects surface from
// EndpointSeedGraph.Build (empty/duplicate name, missing/multiple root, unknown reference, cycle).
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
