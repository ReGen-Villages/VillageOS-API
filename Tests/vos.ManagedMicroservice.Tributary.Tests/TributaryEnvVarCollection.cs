using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Tests that read or mutate the TRIBUTARY_* environment variables share this collection
// so they don't run in parallel. CliArgs.Parse falls back to env vars for the
// WebApplicationFactory<Program>-based HandleEndpointTests; without serialization,
// CliArgsTests clearing those vars in its ctor races with HandleEndpointTests setting them
// in InitializeAsync, crashing the test host (same family of issues as VillageOS Bug #5260
// and the Metabolism Phase 2B fix in MetabolismEnvVarCollection.cs).
[CollectionDefinition(nameof(TributaryEnvVarCollection), DisableParallelization = true)]
public class TributaryEnvVarCollection { }
