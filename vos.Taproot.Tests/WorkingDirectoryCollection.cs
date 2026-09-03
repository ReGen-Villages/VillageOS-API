using Xunit;

namespace vos.Taproot.Tests;

// Tests that read or change the process working directory share this collection so they don't run in
// parallel. The working directory is one value for the whole process, and `pwd` and `cd` are part of
// vos.Taproot's actual interface, so a test of either has to move the real thing rather than a value
// injected into it — the same reason CliEnvVarCollection exists for the environment variables.
[CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
public class WorkingDirectoryCollection { }
