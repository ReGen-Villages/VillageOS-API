using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// One registration serving many addresses, driven through /handle (Feature #5917). The unit-level
// substitution rules live in Helpers/AddressPlaceholdersTests; these pin what the call path does with
// them — which address goes on the wire, and what is refused before anything is called.
public class AddressParameterHandleTests
{
    private const string TileEndpointProperties = """
    {
      "Endpoint.url":        {"Value":"https://tiles.test/tile/{z}/{y}/{x}.png"},
      "Endpoint.httpMethod": {"Value":"GET"}
    }
    """;

    [Fact]
    public async Task Handle_AddressParameters_FillTheStoredAddress()
    {
        var thingId = Guid.NewGuid();
        Uri? requested = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { requested = req.RequestUri; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, TileEndpointProperties)
                ?? RouteKindsFromProperties(req, thingId, TileEndpointProperties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["z"] = "9", ["y"] = "271", ["x"] = "301" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requested.Should().NotBeNull();
        requested!.AbsoluteUri.Should().Be("https://tiles.test/tile/9/271/301.png");
    }

    [Fact]
    public async Task Handle_TwoCallsWithDifferentParameters_ReachTwoAddressesFromOneRegistration()
    {
        var thingId = Guid.NewGuid();
        var requested = new List<string>();
        var registrationWrites = 0;
        var thingsCreated = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test")
            {
                requested.Add(req.RequestUri.AbsoluteUri);
                return Json("{\"ok\":true}");
            }
            if (req.Method == HttpMethod.Put && req.RequestUri.AbsolutePath == $"/api/things/{thingId}/properties")
            {
                registrationWrites++;
                return Json("{}");
            }
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath == "/api/things")
            {
                thingsCreated++;
                return Json("{}");
            }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, TileEndpointProperties)
                ?? RouteKindsFromProperties(req, thingId, TileEndpointProperties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        foreach (var y in new[] { "271", "272" })
        {
            var response = await client.PostAsJsonAsync("/handle", new
            {
                endpointName = "EP",
                addressParameters = new Dictionary<string, string> { ["z"] = "9", ["y"] = y, ["x"] = "301" }
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        requested.Should().Equal(
            "https://tiles.test/tile/9/271/301.png",
            "https://tiles.test/tile/9/272/301.png");
        registrationWrites.Should().Be(0, "a per-call address must not become part of the registration");
        thingsCreated.Should().Be(0, "the catalogue must not grow a registration per address");
    }

    [Fact]
    public async Task Handle_UnfilledPlaceholder_Returns400NamingEachOneBeforeCalling()
    {
        var thingId = Guid.NewGuid();
        var outboundCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { outboundCalls++; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, TileEndpointProperties)
                ?? RouteKindsFromProperties(req, thingId, TileEndpointProperties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["y"] = "271" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("z").And.Contain("x");
        outboundCalls.Should().Be(0, "an address still carrying a placeholder must never be called");
    }

    [Fact]
    public async Task Handle_NoAddressParameters_OnAnAddressWithPlaceholders_Returns400()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, TileEndpointProperties)
            ?? RouteKindsFromProperties(req, thingId, TileEndpointProperties)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("addressParameters");
    }

    [Fact]
    public async Task Handle_ParameterNoPlaceholderNames_IsIgnored()
    {
        // A discovery run passes one site's values to every covering source; a source whose address
        // takes none of them must still be called rather than refused.
        var thingId = Guid.NewGuid();
        Uri? requested = null;
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/fixed?f=json"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { requested = req.RequestUri; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["lat"] = "-25.75", ["lng"] = "28.19" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requested!.AbsoluteUri.Should().Be("https://api.test/fixed?f=json");
    }

    [Fact]
    public async Task Handle_ParameterNameCaseDiffersFromThePlaceholder_StillFills()
    {
        // The values arrive deserialized from the request body, which gives an ordinal dictionary.
        // Only a call-path test can catch the lookup silently disagreeing with every other property
        // map in this service — a unit test is free to hand the helper a comparer no caller uses.
        var thingId = Guid.NewGuid();
        Uri? requested = null;
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/{Site}/summary"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { requested = req.RequestUri; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["site"] = "willow" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requested!.AbsoluteUri.Should().Be("https://api.test/willow/summary");
    }

    [Fact]
    public async Task Handle_ParameterValue_IsEscapedIntoTheAddress()
    {
        var thingId = Guid.NewGuid();
        Uri? requested = null;
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/lookup?name={name}"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { requested = req.RequestUri; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["name"] = "a&admin=true" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requested!.Query.Should().Be("?name=a%26admin%3Dtrue");
    }

    [Fact]
    public async Task Handle_PlaceholdersComposeWithPagingParameters()
    {
        // Paging rewrites the query per page; it must walk the filled address, not the placeholders.
        var thingId = Guid.NewGuid();
        var requested = new List<string>();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/{layer}/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Esri.pagingKind":     {"Value":"offset"},
          "Esri.offsetParam":    {"Value":"resultOffset"},
          "Esri.hasMorePath":    {"Value":"exceededTransferLimit"},
          "Esri.itemsPath":      {"Value":"features"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "features.test")
            {
                requested.Add(req.RequestUri.AbsolutePath);
                return req.RequestUri.Query.Contains("resultOffset=1")
                    ? Json("{\"features\":[],\"exceededTransferLimit\":false}")
                    : Json("{\"features\":[{\"id\":1}],\"exceededTransferLimit\":true}");
            }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["layer"] = "3" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        requested.Should().HaveCountGreaterThan(1).And.OnlyContain(path => path == "/3/query");
    }
}
