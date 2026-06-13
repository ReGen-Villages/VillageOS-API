using Xunit;

namespace vos.Taproot.Tests;

// Tests that read or mutate the VOS_BROKER_URL / VOS_API_KEY environment variables share
// this collection so they don't run in parallel. Without serialization, set/restore
// windows in one test race with reads in another and fail intermittently (e.g. under
// `dotnet test` from the solution root). These env vars are part of vos.Taproot's actual
// production interface (set by users to configure the CLI), so they can't be replaced
// with IConfiguration injection the way the microservice CliArgs were.
[CollectionDefinition(nameof(CliEnvVarCollection), DisableParallelization = true)]
public class CliEnvVarCollection { }
