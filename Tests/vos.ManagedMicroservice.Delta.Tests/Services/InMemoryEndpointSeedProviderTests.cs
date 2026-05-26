using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests.Services;

/// <summary>
/// Unit tests for the test-only <see cref="InMemoryEndpointSeedProvider"/>. The provider exists
/// so <see cref="DeltaWebApplicationFactory"/> can supply a per-instance seed without writing
/// to <c>AppContext.BaseDirectory</c>. Behavior must mirror the file-loader's throw contract
/// when the JSON is unusable, so <c>EndpointSeedBootTests.LoadEndpointSeed_MalformedJson_ThrowsAtStartup</c>
/// keeps pinning the host-startup-fail contract.
/// </summary>
public class InMemoryEndpointSeedProviderTests
{
    [Fact]
    public void LoadSeed_ValidJson_ReturnsParsedSeed()
    {
        var provider = new InMemoryEndpointSeedProvider("""
            {
              "name": "Endpoint",
              "properties": { "url": "https://default.example/", "httpMethod": "GET" }
            }
            """);

        var seed = provider.LoadSeed();

        seed.Name.Should().Be("Endpoint");
        seed.Properties.Should().ContainKey("url");
        seed.Properties!.Should().ContainKey("httpMethod");
    }

    [Fact]
    public void LoadSeed_MalformedJson_Throws()
    {
        var provider = new InMemoryEndpointSeedProvider("{ not valid json at all");

        var act = () => provider.LoadSeed();

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void LoadSeed_EmptyName_Throws()
    {
        // Mirrors EndpointSeedLoader.Load's "non-empty Name" validity check.
        var provider = new InMemoryEndpointSeedProvider("""{ "name": "", "properties": {} }""");

        var act = () => provider.LoadSeed();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadSeed_CaseInsensitivePropertyNames_ReturnsParsedSeed()
    {
        // Match EndpointSeedLoader's PropertyNameCaseInsensitive = true setting.
        var provider = new InMemoryEndpointSeedProvider("""
            { "Name": "Endpoint", "Properties": { "url": "https://x/" } }
            """);

        var seed = provider.LoadSeed();

        seed.Name.Should().Be("Endpoint");
        seed.Properties.Should().ContainKey("url");
    }
}
