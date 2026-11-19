using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace vos.Tests.Shared;

public abstract class NodeServiceStartupTests<TFactory, TProgram>
    where TFactory : ServiceWebApplicationFactory<TProgram>, new()
    where TProgram : class
{
    protected abstract string ServiceName { get; }

    [Fact]
    public async Task A_node_envelope_on_handle_is_answered_with_the_nodes_result()
    {
        await using var factory = new TFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle",
            new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid(), @params = new { }, inputs = new { } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Anything_else_on_handle_is_refused_naming_the_service()
    {
        await using var factory = new TFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "not a node envelope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ServiceName, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_manifest_lists_the_nodes_ports()
    {
        await using var factory = new TFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var ports = await client.GetFromJsonAsync<JsonElement>("/manifest");

        Assert.Equal(JsonValueKind.Array, ports.ValueKind);
        Assert.NotEqual(0, ports.GetArrayLength());
    }

    [Fact]
    public async Task Health_names_the_service()
    {
        await using var factory = new TFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonElement>("/health");

        Assert.Equal(ServiceName, health.GetProperty("service").GetString());
    }
}
