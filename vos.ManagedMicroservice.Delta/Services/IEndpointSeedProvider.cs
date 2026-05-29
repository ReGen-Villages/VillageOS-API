using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Source of the Delta service's endpoint-template graph, resolved at host startup.
/// The production implementation reads a model seed document (<c>seed.json</c>: things +
/// relationships) from disk; tests supply an in-memory variant so each
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{T}"/> instance owns its own
/// seed without sharing <c>AppContext.BaseDirectory</c> state.
/// </summary>
public interface IEndpointSeedProvider
{
    EndpointSeedGraph LoadGraph();
}
