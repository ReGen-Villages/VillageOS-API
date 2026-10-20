using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using Xunit;
using DeltaClient = vos.Service.Delta.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Delta's selector and its reading of the resolved properties are both decided by the broker, so a stand-in that
// answers either the way Delta expects proves nothing about the real one.
public class DeltaReadsAnEndpointTemplateTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public DeltaReadsAnEndpointTemplateTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task Delta_reads_the_named_template_and_the_bound_root_with_every_key_resolved_up_the_chain()
    {
        var delta = new DeltaClient(_engine.ClientFactory, NullLogger<DeltaClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var binds = await _engine.DeclareOnceAsync("binds");
        await _engine.DeclareAsync("endpoint role", identifier: EndpointTemplateTree.EndpointRoleId);
        var root = await _engine.DeclareAsync("Endpoint", isArchetype: true,
            new Dictionary<string, object?> { ["url"] = "", ["httpMethod"] = "GET", ["responseTransform"] = "" });
        var child = await _engine.DeclareAsync("EsriEndpoint", isArchetype: true,
            new Dictionary<string, object?> { ["layer"] = "" });
        await _engine.RelateAsync(child, isPredicate, root);
        (await _engine.Admin.PutAsJsonAsync($"/api/things/{child}/properties",
            new { Name = "httpMethod", Type = "vos.String", Value = "POST" })).EnsureSuccessStatusCode();
        await _engine.RelateAsync(EndpointTemplateTree.EndpointRoleId, binds, root);
        var reader = new EndpointTemplateReader(
            new SubscriptionClient(_engine.ClientFactory, NullLogger.Instance, TheEngine.Url, _engine.AdminToken),
            delta, NullLogger<EndpointTemplateReader>.Instance);

        var (named, namedRefusal) = await reader.ReadAsync("EsriEndpoint");
        var (bare, bareRefusal) = await reader.ReadAsync(null);

        namedRefusal.Should().BeNull();
        named!.Id.Should().Be(child);
        named.IsPredicateId.Should().Be(isPredicate);
        foreach (var key in new[] { "url", "responseTransform", "layer" })
            Value(named.Properties, key).ValueKind.Should().Be(JsonValueKind.String, key);
        Value(named.Properties, "httpMethod").GetString().Should().Be("POST");
        bareRefusal.Should().BeNull();
        bare!.Id.Should().Be(root);
        Value(bare.Properties, "httpMethod").GetString().Should().Be("GET");
    }

    private static JsonElement Value(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        EffectivePropertyResolver.TryGetEffectiveProperty(properties, name, out var value, out var conflicts)
            .Should().BeTrue($"'{name}' resolves to one value; matches: {string.Join(", ", conflicts ?? [])}");
        return value;
    }
}
