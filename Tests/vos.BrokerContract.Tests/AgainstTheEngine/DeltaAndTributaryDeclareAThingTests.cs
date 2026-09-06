using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Delta.Models;
using vos.Service.Shared;
using Xunit;
using DeltaClient = vos.Service.Delta.Services.MyceliumClient;
using TributaryClient = vos.Service.Tributary.Services.MyceliumClient;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

/// <summary>
/// The two services that create a Thing carrying properties, writing into the real engine.
///
/// Both answered a refused create by logging it and carrying on — Delta loses a template and every
/// endpoint beneath it, Tributary loses the reading that first sighted an entity — so nothing but a
/// live run said the writes were wrong, and neither path runs when the Things are already seeded
/// (Bug #6930). Both suites answered the create with a stand-in that accepts any body.
/// </summary>
public class DeltaAndTributaryDeclareAThingTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public DeltaAndTributaryDeclareAThingTests(TheEngine engine) => _engine = engine;

    private DeltaClient Delta() => new(
        _engine.ClientFactory, NullLogger<DeltaClient>.Instance, TheEngine.Url, _engine.AdminToken);

    private TributaryClient Tributary() => new(
        _engine.ClientFactory, NullLogger<TributaryClient>.Instance, TheEngine.Url, _engine.AdminToken);

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public async Task Delta_declares_an_endpoint_template_with_the_keys_it_narrows()
    {
        var created = await Delta().CreateThingAsync(new RegisterEndpointRequest
        {
            Name = $"Endpoint_{Guid.NewGuid():N}",
            Properties = new Dictionary<string, object>
            {
                ["httpMethod"] = "POST",
                ["requestContentType"] = "application/x-www-form-urlencoded",
            },
        });

        created.Should().NotBeNull();
        (await _engine.ValueOfAsync(created!.Value.Id, "httpMethod")).Should().Be("POST");
    }

    /// <summary>A key an endpoint must supply is declared by name with nothing in it, which is a
    /// property carrying an empty string — and an empty string is a value like any other, so it needs
    /// its type as much as a filled one does.</summary>
    [Fact]
    public async Task Delta_declares_a_key_an_endpoint_must_supply_with_nothing_in_it()
    {
        var created = await Delta().CreateThingAsync(new RegisterEndpointRequest
        {
            Name = $"Endpoint_{Guid.NewGuid():N}",
            Properties = new Dictionary<string, object> { ["token"] = string.Empty },
        });

        created.Should().NotBeNull();
        (await _engine.ValueOfAsync(created!.Value.Id, "token")).Should().BeEmpty();
    }

    /// <summary>Tributary forwards what a caller sent, so the values are whatever arrived as JSON —
    /// the case the type has to be worked out for rather than known.</summary>
    [Fact]
    public async Task Tributary_declares_an_entity_from_the_reading_that_first_sighted_it()
    {
        var created = await Tributary().CreateThingAsync($"Reservoir_{Guid.NewGuid():N}",
            new Dictionary<string, object?>
            {
                ["storedM3"] = Json("214.5"),
                ["condition"] = Json("\"filling\""),
                ["isOverflowing"] = Json("false"),
            });

        created.Should().NotBeNull();
        var read = (await _engine.ReadAsync(created!.Value.Id)).GetProperty("Properties");
        read.GetProperty("storedM3").GetProperty("typeInfo").GetString().Should().Be("vos.Double");
        read.GetProperty("storedM3").GetProperty("value").GetDouble().Should().Be(214.5);
        read.GetProperty("condition").GetProperty("value").GetString().Should().Be("filling");
        read.GetProperty("isOverflowing").GetProperty("typeInfo").GetString().Should().Be("vos.Boolean");
    }

    /// <summary>A reading that arrives whole is still a measurement, and the next one may not be. The
    /// property's type is decided by the first write, so recording 3 as an integer is a property that
    /// cannot hold 3.5 afterwards.</summary>
    [Fact]
    public async Task Tributary_holds_a_whole_reading_as_a_measurement_so_the_next_one_fits()
    {
        var client = Tributary();
        var name = $"Reservoir_{Guid.NewGuid():N}";
        var created = await client.CreateThingAsync(
            name, new Dictionary<string, object?> { ["storedM3"] = Json("3") });

        created.Should().NotBeNull();
        var written = await _engine.Admin.PutAsJsonAsync(
            $"/api/things/{created!.Value.Id}/properties",
            new { Name = "storedM3", Type = "vos.Double", Value = 3.5 });

        written.EnsureSuccessStatusCode();
        (await _engine.ValueOfAsync(created.Value.Id, "storedM3")).Should().Be("3.5");
    }

    /// <summary>Every name the writer can send is one the engine takes. The engine serves the names it
    /// accepts, so this is held against what it says rather than against a second list kept by
    /// hand — the same way the client's property-type set is checked.</summary>
    [Fact]
    public async Task Every_type_the_services_can_write_is_one_the_engine_accepts()
    {
        var accepted = await _engine.Admin.GetFromJsonAsync<string[]>("/api/properties/types");

        TypedProperties.TypeNames.Should().BeSubsetOf(accepted!);
    }
}
