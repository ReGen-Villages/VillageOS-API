using FluentAssertions;
using Xunit;

namespace vos.Service.Delta.Tests;

// Integration tests that pin the boot-time fail-fast contract: when the model seed.json
// cannot be parsed or forms an invalid template graph, the host fails to start.
// EndpointSeedLoader and EndpointSeedGraph are unit-tested directly; these assert that
// Program.cs surfaces their exceptions to host construction so a misconfigured deployment fails
// loudly rather than starting with no usable seed.
public class EndpointSeedBootTests
{
    [Fact]
    public async Task LoadEndpointSeed_MalformedJson_ThrowsAtStartup()
    {
        await using var factory = new DeltaWebApplicationFactory { SeedJson = "{ not valid json at all" };
        await factory.InitializeAsync();

        // CreateClient triggers host construction → LoadGraph → throws.
        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>();
    }

    [Fact]
    public async Task LoadEndpointSeed_ValidMultiTemplateGraph_StartsUp()
    {
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """
            {
              "name": "Endpoint Templates",
              "things": [
                { "name": "Endpoint", "properties": { "url": "https://x/", "httpMethod": "GET" } },
                { "name": "EsriEndpoint", "properties": { "httpMethod": "POST" } }
              ],
              "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
            }
            """
        };
        await factory.InitializeAsync();

        // Host construction succeeds; /health is reachable.
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task LoadEndpointSeed_CyclicTemplateGraph_ThrowsAtStartup()
    {
        await using var factory = new DeltaWebApplicationFactory
        {
            SeedJson = """
            {
              "things": [
                { "name": "Endpoint", "properties": {} },
                { "name": "A", "properties": {} },
                { "name": "B", "properties": {} }
              ],
              "relationships": [
                { "subject": "A", "predicate": "is", "target": "B" },
                { "subject": "B", "predicate": "is", "target": "A" }
              ]
            }
            """
        };
        await factory.InitializeAsync();

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>();
    }
}
