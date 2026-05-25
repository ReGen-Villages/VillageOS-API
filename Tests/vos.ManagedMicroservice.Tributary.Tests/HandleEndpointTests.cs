using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

// Tests for the /handle endpoint in vos.ManagedMicroservice.Tributary/Program.cs.
// Each test wires up a per-scenario HandlerCallback on the factory so the broker calls
// (FindThingByNameAsync, GetEffectivePropertiesAsync, SetThingPropertyAsync, CreateThing,
// CreateRelationship) AND the outbound endpoint call all resolve through the same handler.
public class HandleEndpointTests
{
    // ---------- Request validation ----------

    [Fact]
    public async Task Handle_MissingEndpointName_Returns400()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("non-empty endpointName");
    }

    [Fact]
    public async Task Handle_ThingNotFound_Returns404()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            // Broker GET /api/things?name=Unknown returns empty (non-object root → null thing)
            if (req.RequestUri!.AbsolutePath == "/api/things")
                return Json("null");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "Unknown" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Endpoint thing not found");
    }

    [Fact]
    public async Task Handle_EffectivePropertiesNull_Returns500()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things")
                return Json($$"""{"Id":"{{thingId}}","Name":"Endpoint1"}""");
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/effective-properties")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "Endpoint1" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Endpoint resolution failed");
    }

    // ---------- responseTransform override (request-level) ----------

    [Fact]
    public async Task Handle_InvalidOverrideTransform_Returns400()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, EmptyProps())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            responseTransform = "{ this is not valid jsonata @"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid responseTransform");
    }

    [Fact]
    public async Task Handle_OverrideTransformPersistFails_Returns502()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, EmptyProps())
                ?? (req.Method == HttpMethod.Put
                    && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties"
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            responseTransform = "{\"v\":1}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Failed to persist responseTransform");
    }

    // ---------- TryGetEffectiveProperty branches ----------

    [Fact]
    public async Task Handle_MissingUrlAndMethod_Returns400()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, EmptyProps())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("missing required properties");
    }

    [Fact]
    public async Task Handle_AmbiguousUrlProperties_Returns400WithConflicts()
    {
        var thingId = Guid.NewGuid();
        // Two prefixed url candidates that both suffix-match "url" — no exact key — conflict.
        var props = """
        {
          "Endpoint.url": {"Value":"https://a"},
          "Other.url":    {"Value":"https://b"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ambiguous properties for url/httpMethod");
    }

    [Fact]
    public async Task Handle_AmbiguousResponseTransform_Returns400WithConflicts()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "A.responseTransform": {"Value":"$.a"},
          "B.responseTransform": {"Value":"$.b"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ambiguous properties for responseTransform");
    }

    [Fact]
    public async Task Handle_InvalidUri_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"not-a-url"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid endpoint url");
    }

    [Fact]
    public async Task Handle_UnsupportedHttpMethod_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"BREW"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProps(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported httpMethod");
    }

    // ---------- Successful endpoint dispatch ----------

    [Fact]
    public async Task Handle_GetEndpointWithoutTransform_ReturnsRawBody()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/data"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? (req.RequestUri!.Host == "api.test" && req.Method == HttpMethod.Get
                    ? Json("{\"value\":42}")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"value\":42}");
    }

    [Fact]
    public async Task Handle_GetEndpointWithStoredTransform_ReturnsTransformedBody()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":              {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":       {"Value":"GET"},
          "Endpoint.responseTransform":{"Value":"{\"v\":value}"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? (req.RequestUri!.Host == "api.test"
                    ? Json("{\"value\":42}")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"v\":42}");
    }

    [Fact]
    public async Task Handle_PostEndpointSendsBody_ReturnsRawBody()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/post"},
          "Endpoint.httpMethod": {"Value":"POST"}
        }
        """;
        string? sentBody = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test" && req.Method == HttpMethod.Post)
            {
                sentBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("{\"ok\":true}");
            }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            body = new { hello = "world" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sentBody.Should().NotBeNull();
        sentBody!.Should().Contain("\"hello\"").And.Contain("\"world\"");
    }

    [Fact]
    public async Task Handle_OverrideTransformWith2xx_CreatesObservations()
    {
        var thingId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/obs"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            // Persist override
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
                return new HttpResponseMessage(HttpStatusCode.OK);
            // Find "observed" predicate
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=observed"))
                return Json($$"""{"Id":"{{observedId}}","Name":"observed"}""");
            // Create observation thing
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json($$"""{"Id":"{{createdId}}","Name":"Obs"}""");
            // Create relationship
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.OK);
            // Endpoint call
            if (req.RequestUri.Host == "api.test")
                return Json("{\"raw\":true}");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            responseTransform = "{\"name\":\"Obs\",\"properties\":{\"v\":1}}"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":true");
        body.Should().Contain("\"observedCount\":1");
    }

    [Fact]
    public async Task Handle_OverrideTransformWith2xxIngestFails_Returns400WithError()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri!.Host == "api.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-json", Encoding.UTF8, "application/json")
                };
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            responseTransform = "$"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("transform failed");
    }

    [Fact]
    public async Task Handle_StoredTransformIsInvalidJsonata_Returns502()
    {
        // ResponseTransform is persisted on the thing (not in request), but is unparseable.
        // SetThingPropertyAsync path is not hit (no override on request). The stored value
        // fails the JsonataQuery ctor in the catch-bound try, which returns 502.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":              {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":       {"Value":"GET"},
          "Endpoint.responseTransform":{"Value":"{ definitely not @ jsonata"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                return Json("{\"x\":1}");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("transform failed");
    }

    [Fact]
    public async Task Handle_StoredTransformOnNonJsonBody_Returns502()
    {
        // Stored transform exists, endpoint returns non-JSON → TryTransform fails → 502.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":              {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":       {"Value":"GET"},
          "Endpoint.responseTransform":{"Value":"$"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
                };
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Handle_OutboundCallThrows_Returns502()
    {
        // The MockHttpMessageHandler's callback throws — surfaces as an exception in
        // CallEndpointAsync's await, hits the outer try/catch in /handle, returns 502.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/throws"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                throw new HttpRequestException("boom");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProps(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Endpoint call failed");
    }

    // ---------- /health and /shutdown ----------

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"service\":\"Tributary\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Shutting down Tributary");
    }

    // ---------- Helpers ----------

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage? RouteFindThing(HttpRequestMessage req, Guid id, string name)
    {
        if (req.Method == HttpMethod.Get
            && req.RequestUri!.AbsolutePath == "/api/things"
            && req.RequestUri.Query.Contains($"name={name}"))
            return Json($$"""{"Id":"{{id}}","Name":"{{name}}"}""");
        return null;
    }

    private static HttpResponseMessage? RouteEffectiveProps(HttpRequestMessage req, Guid id, string jsonObject)
    {
        if (req.Method == HttpMethod.Get
            && req.RequestUri!.AbsolutePath == $"/api/things/{id}/effective-properties")
            return Json(jsonObject);
        return null;
    }

    private static string EmptyProps() => "{}";
}
