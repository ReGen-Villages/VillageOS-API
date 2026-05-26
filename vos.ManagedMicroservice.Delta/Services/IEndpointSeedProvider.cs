using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Services;

/// <summary>
/// Source of the Delta service's endpoint seed template, resolved at host startup.
/// The production implementation reads <c>seed.json</c> from disk; tests supply an
/// in-memory variant so each <see cref="WebApplicationFactory{T}"/> instance owns its
/// own seed without sharing <c>AppContext.BaseDirectory</c> state.
/// </summary>
public interface IEndpointSeedProvider
{
    RegisterEndpointRequest LoadSeed();
}
