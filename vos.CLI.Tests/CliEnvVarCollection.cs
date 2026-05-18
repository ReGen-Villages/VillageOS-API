using Xunit;

namespace vos.CLI.Tests;

// Tests that read or mutate the VOS_BROKER_URL / VOS_API_KEY environment variables share
// this collection so they don't run in parallel. Without serialization, set/restore
// windows in one test race with reads in another and fail intermittently (e.g. under
// `dotnet test` from the solution root). Same pattern used in
// Tests/vos.ManagedMicroservice.Metabolism.Tests/MetabolismEnvVarCollection.cs.
[CollectionDefinition(nameof(CliEnvVarCollection), DisableParallelization = true)]
public class CliEnvVarCollection { }
