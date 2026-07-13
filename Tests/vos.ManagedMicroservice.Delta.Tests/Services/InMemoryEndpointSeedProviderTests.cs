using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests.Services;

// Unit tests for the test-only InMemoryEndpointSeedProvider. The provider exists so
// DeltaWebApplicationFactory can supply a per-instance model seed without writing to
// AppContext.BaseDirectory. Behavior must mirror the file-loader's throw contract when the
// JSON is unusable, so EndpointSeedBootTests keeps pinning the host-startup-fail contract.
public class InMemoryEndpointSeedProviderTests
{
    [Fact]
    public void LoadGraph_ValidModelJson_ReturnsRootTemplate()
    {
        var provider = new InMemoryEndpointSeedProvider("""
            {
              "name": "Endpoint Templates",
              "things": [ { "name": "Endpoint", "properties": { "url": "https://default.example/", "httpMethod": "GET" } } ],
              "relationships": []
            }
            """);

        var graph = provider.LoadGraph();

        graph.Root.Name.Should().Be("Endpoint");
        graph.Root.Properties.Should().ContainKey("url");
        graph.Root.Properties!.Should().ContainKey("httpMethod");
    }

    [Fact]
    public void LoadGraph_MultiTemplateModel_BuildsGraphKeyedByName()
    {
        var provider = new InMemoryEndpointSeedProvider("""
            {
              "things": [
                { "name": "Endpoint", "properties": { "url": "https://x/", "httpMethod": "GET" } },
                { "name": "EsriEndpoint", "properties": { "httpMethod": "POST" } }
              ],
              "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
            }
            """);

        var graph = provider.LoadGraph();

        graph.Templates.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint" });
        graph.ParentName("EsriEndpoint").Should().Be("Endpoint");
    }

    [Fact]
    public void LoadGraph_MalformedJson_Throws()
    {
        var provider = new InMemoryEndpointSeedProvider("{ not valid json at all");

        var act = () => provider.LoadGraph();

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void LoadGraph_EmptyThingName_Throws()
    {
        // Surfaces from EndpointSeedGraph.Build's non-empty-name rule.
        var provider = new InMemoryEndpointSeedProvider("""
            { "things": [ { "name": "", "properties": {} } ], "relationships": [] }
            """);

        var act = () => provider.LoadGraph();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadGraph_CaseInsensitivePropertyNames_ReturnsParsedSeed()
    {
        var provider = new InMemoryEndpointSeedProvider("""
            { "Things": [ { "Name": "Endpoint", "Properties": { "url": "https://x/" } } ], "Relationships": [] }
            """);

        var graph = provider.LoadGraph();

        graph.Root.Name.Should().Be("Endpoint");
        graph.Root.Properties.Should().ContainKey("url");
    }
}
