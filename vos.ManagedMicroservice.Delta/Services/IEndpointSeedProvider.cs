using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

// Source of the Delta service's endpoint-template graph, resolved at host startup.
// The production implementation reads a model seed document (seed.json: things +
// relationships) from disk; tests supply an in-memory variant so each
// Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{T} instance owns its own
// seed without sharing AppContext.BaseDirectory state.
public interface IEndpointSeedProvider
{
    EndpointSeedGraph LoadGraph();
}
