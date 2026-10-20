using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Delta.Models;
using Xunit;
using DeltaClient = vos.Service.Delta.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The values a registration states arrive in its request as JSON, and Delta writes each one onto the Thing it has
// just joined to its template. The broker takes only its own type names on that write.
public class DeltaWritesARegistrationsPropertiesTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public DeltaWritesARegistrationsPropertiesTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task Delta_writes_each_value_a_registration_states_onto_the_registration()
    {
        var delta = new DeltaClient(_engine.ClientFactory, NullLogger<DeltaClient>.Instance, TheEngine.Url, _engine.AdminToken);
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var template = await _engine.DeclareAsync($"Endpoint_{Guid.NewGuid():N}", isArchetype: true,
            new Dictionary<string, object?> { ["url"] = "", ["pageSize"] = 0, ["paged"] = false });
        var registered = await delta.CreateThingAsync(new RegisterEndpointRequest { Name = $"source_{Guid.NewGuid():N}" });
        await _engine.RelateAsync(registered!.Value.Id, isPredicate, template);
        var stated = JsonDocument.Parse("""{ "url": "https://source.example/data", "pageSize": 500, "paged": true }""").RootElement;

        foreach (var value in stated.EnumerateObject())
            (await delta.SetThingPropertyAsync(registered.Value.Id, value.Name, value.Value)).Should().BeTrue(value.Name);

        var read = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{registered.Value.Id}/properties");
        Value(read, "url").GetString().Should().Be("https://source.example/data");
        Value(read, "pageSize").GetDouble().Should().Be(500);
        Value(read, "paged").GetBoolean().Should().BeTrue();
    }

    private static JsonElement Value(JsonElement resolved, string name) =>
        resolved.EnumerateObject().Single(property => property.Name.Split('.')[^1] == name).Value.GetProperty("Value");
}
