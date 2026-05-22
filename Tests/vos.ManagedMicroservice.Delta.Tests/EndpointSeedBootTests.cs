using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Integration test that pins the boot-time fail-fast contract: when <c>seed.json</c>
/// cannot be parsed into a valid <see cref="vos.ManagedMicroservice.Delta.Models.RegisterEndpointRequest"/>,
/// the host fails to start. <c>EndpointSeedLoader</c> itself is unit-tested directly
/// (see <c>Helpers/EndpointSeedLoaderTests.cs</c>); this test asserts that Program.cs
/// surfaces the loader's exception to host construction so a misconfigured deployment
/// fails loudly rather than starting with no usable seed.
///
/// Moved from <c>CoverageGapTests.cs</c> under Task #5437. The <c>MalformedSeedFactory</c>
/// sub-class was inlined by setting <see cref="DeltaWebApplicationFactory.SeedJson"/>
/// directly on a base-factory instance.
/// </summary>
[Collection(nameof(DeltaEnvVarCollection))]
public class EndpointSeedBootTests
{
    [Fact]
    public async Task LoadEndpointSeed_MalformedJson_ThrowsAtStartup()
    {
        await using var factory = new DeltaWebApplicationFactory { SeedJson = "{ not valid json at all" };
        await factory.InitializeAsync();

        // CreateClient triggers host construction → LoadEndpointSeed → throws.
        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>();
    }
}
