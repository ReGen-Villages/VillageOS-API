using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

// Tests that read or mutate the METABOLISM_* environment variables share this collection
// so they don't run in parallel. CliArgs.Parse falls back to env vars for the
// WebApplicationFactory<Program>-based EndpointMapperTests; without serialization,
// CliArgsTests clearing those vars in its ctor races with EndpointMapperTests setting them
// in InitializeAsync, crashing the test host (same family of issues as VillageOS Bug #5260).
[CollectionDefinition(nameof(MetabolismEnvVarCollection), DisableParallelization = true)]
public class MetabolismEnvVarCollection { }
