using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

// Tests that construct a DeltaWebApplicationFactory share this collection so they don't
// run in parallel. The factory writes a synthetic seed.json into AppContext.BaseDirectory
// (a process-global path) in InitializeAsync and deletes it in DisposeAsync — parallel
// factories would clobber each other's seed file. A per-test seed path is the proper fix
// (future work) but the collection is the minimal serialization for now.
[CollectionDefinition(nameof(DeltaFactoryCollection), DisableParallelization = true)]
public class DeltaFactoryCollection { }
