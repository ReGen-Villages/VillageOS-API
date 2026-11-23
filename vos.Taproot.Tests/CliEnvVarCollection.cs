using Xunit;

namespace vos.Taproot.Tests;

// Tests that read or change VOS_MYCELIUM_URL, VOS_API_KEY or VOS_INSECURE_TLS share this collection so they never run
// at the same time, because these variables are how a person configures the command line and cannot be passed in instead.
[CollectionDefinition(nameof(CliEnvVarCollection), DisableParallelization = true)]
public class CliEnvVarCollection { }
