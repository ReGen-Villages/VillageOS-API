using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.Service.Tributary.Helpers;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Tests for the /handle endpoint in vos.Service.Tributary/Program.cs.
// Each test wires up a per-scenario HandlerCallback on the factory so Mycelium calls
// (FindThingByNameAsync, GetEffectivePropertiesAsync, CreateThing, CreateRelationship,
// SubmitObservations) AND the outbound endpoint call all resolve through the same handler.
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
            // Mycelium GET /api/things?name=Unknown returns empty (non-object root → null thing)
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
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/properties")
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
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        var outboundCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outboundCalls++; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            responseTransform = "{ this is not valid jsonata @"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid responseTransform");
        outboundCalls.Should().Be(0, "an expression that cannot compile is refused before the source is called");
    }

    // ---------- TryGetEffectiveProperty branches ----------

    [Fact]
    public async Task Handle_MissingUrlAndMethod_Returns400()
    {
        var thingId = Guid.NewGuid();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, EmptyProps())
            ?? RouteKindsFromProperties(req, thingId, EmptyProps())
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
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
    public async Task Handle_BlankTransformDeclaredOnTheTemplate_ReturnsRawBody()
    {
        // A blank value makes the key admissible for a registration without supplying an
        // expression, so the root template can declare it and a plain fetch still passes through.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":""}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? (req.RequestUri!.Host == "api.test"
                    ? Json("{\"value\":42}")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"value\":42}");
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
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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

    // ---------- The reshape expression in effect drives ingest (Bug #6051) ----------

    // Tributary cannot tell an own property from an inherited one, and must not try: Mycelium's
    // resolved view qualifies an inherited key with the template that declares it, and both
    // spellings arrive through the same suffix match.
    [Theory]
    [InlineData("responseTransform")]
    [InlineData("Endpoint.responseTransform")]
    public async Task Handle_TransformOnTheEndpoint_IngestsOntoTheNamedThing(string transformKey)
    {
        var thingId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var props = $$"""
        {
          "Endpoint.url":        {"Value":"https://api.test/forecast"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "{{transformKey}}":    {"Value":"{\"name\": \"ExampleSite\", \"properties\": {\"precipitation\": hourly.precipitation[0]}, \"observedAt\": hourly.time[0]}"}
        }
        """;
        string? observations = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == $"/api/things/{siteId}/observations")
            {
                observations = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{"accepted":1}""");
            }
            if (req.RequestUri!.Host == "api.test")
                return Json("""{"hourly":{"time":["2026-07-09T00:00"],"precipitation":[3.4]}}""");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteFindThing(req, siteId, "ExampleSite")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"entitiesTouched\":1").And.Contain("\"observationsSubmitted\":1");
        observations.Should().NotBeNull();
        observations!.Should().Contain("\"property\":\"precipitation\"").And.Contain("\"value\":3.4");
    }

    [Fact]
    public async Task Handle_OverrideTransform_IngestsAndLeavesTheRegistrationUnchanged()
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
        var registrationWrites = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties")
            {
                registrationWrites++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (req.RequestUri!.AbsolutePath == "/api/things" && req.Method == HttpMethod.Get
                && req.RequestUri.Query.Contains("name=observed"))
                return Json($$"""{"Id":"{{observedId}}","Name":"observed"}""");
            if (req.RequestUri.AbsolutePath == "/api/things" && req.Method == HttpMethod.Post)
                return Json($$"""{"Id":"{{createdId}}","Name":"Obs"}""");
            if (req.RequestUri.AbsolutePath.Contains("/mode") && req.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.AbsolutePath.EndsWith("/observations") && req.Method == HttpMethod.Post)
                return Json("""{"accepted":1}""");
            if (req.RequestUri.AbsolutePath == "/api/relationships" && req.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.OK);
            if (req.RequestUri.Host == "api.test")
                return Json("{\"raw\":true}");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
        body.Should().Contain("\"entitiesTouched\":1");
        registrationWrites.Should().Be(0,
            "an expression supplied on the request reshapes that call only — persisting it would "
            + "change what every later caller of the same source receives");
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
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
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
    public async Task Handle_TransformOnTheEndpointIsInvalidJsonata_Returns400BeforeCalling()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":              {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":       {"Value":"GET"},
          "Endpoint.responseTransform":{"Value":"{ definitely not @ jsonata"}
        }
        """;
        var outboundCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outboundCalls++; return Json("{\"x\":1}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid responseTransform");
        outboundCalls.Should().Be(0, "a registration that cannot compile is refused before the source is called");
    }

    [Fact]
    public async Task Handle_TransformOnTheEndpointOverNonJsonBody_Returns400()
    {
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
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("transform failed");
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
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Endpoint call failed");
    }

    // ---------- What the provider answered reaches the caller (Bug #6831) ----------

    [Fact]
    public async Task Handle_ProviderRefusesTheCall_AnswersWithItsStatusAndWords()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {\"climateZone\": code}}"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("this caller may not read the catalogue", Encoding.UTF8, "text/plain")
                };
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a refusal answered as 200 reads to a run as a call that resolved and wrote nothing");
        (await response.Content.ReadAsStringAsync())
            .Should().Be("this caller may not read the catalogue");
    }

    [Fact]
    public async Task Handle_ProviderHoldsNothing_StaysAnAnswerThatWritesNothing()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {\"hazardLevel\": level}}"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    // A body the expression above would happily reshape, so the 404 is the only thing
                    // keeping it away from the ingest.
                    Content = new StringContent("""{"level":"high"}""", Encoding.UTF8, "application/json")
                };
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a portal holding nothing about a subject has answered, and asking again every quarter of an hour never gets a different answer");
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"level":"high"}""",
            "the body comes back untouched, so nothing was reshaped and nothing was ingested");
    }

    [Fact]
    public async Task Handle_NoUserAgentDeclared_StillNamesTheCallerToTheProvider()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        outbound.Should().NotBeNull();
        outbound!.Headers.GetValues("User-Agent").Should().ContainSingle()
            .Which.Should().Be(OutboundRequest.DefaultUserAgent);
    }

    // ---------- Base request capabilities (Task #5469) ----------

    [Fact]
    public async Task Handle_CustomHeaders_AppliedToOutboundRequest()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.headers":    {"Value":{"Authorization":"Bearer abc","X-Api-Version":"2"}}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound.Should().NotBeNull();
        outbound!.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer abc");
        outbound.Headers.GetValues("X-Api-Version").Should().ContainSingle().Which.Should().Be("2");
    }

    [Fact]
    public async Task Handle_QueryParams_MergedIntoOutboundUri()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":         {"Value":"https://api.test/q?where=1=1"},
          "Endpoint.httpMethod":  {"Value":"GET"},
          "Endpoint.queryParams": {"Value":{"f":"json","outFields":"*"}}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound.Should().NotBeNull();
        var decoded = Uri.UnescapeDataString(outbound!.RequestUri!.Query);
        decoded.Should().Contain("where=1=1").And.Contain("f=json").And.Contain("outFields=*");
    }

    [Fact]
    public async Task Handle_RequestContentType_AppliedToPostBody()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":                {"Value":"https://api.test/x"},
          "Endpoint.httpMethod":         {"Value":"POST"},
          "Endpoint.requestContentType": {"Value":"application/xml"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            body = new { hello = "world" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.Content.Should().NotBeNull();
        outbound.Content!.Headers.ContentType!.MediaType.Should().Be("application/xml");
    }

    [Fact]
    public async Task Handle_DefaultsPreserved_WhenNoBaseCapabilitiesConfigured()
    {
        // An endpoint defining none of the new properties behaves exactly as before:
        // no extra headers, URL unchanged, JSON body, request still succeeds.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"POST"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP", body = new { a = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.RequestUri!.Query.Should().BeEmpty();
        outbound.Headers.Should().NotContain(h => h.Key == "Authorization");
        outbound.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Handle_AmbiguousHeaders_Returns400()
    {
        var thingId = Guid.NewGuid();
        // Two namespaced keys both suffix-match "headers" — ambiguous, like url/httpMethod.
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "A.headers":           {"Value":{"X":"1"}},
          "B.headers":           {"Value":{"Y":"2"}}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ambiguous properties for headers");
    }

    [Theory]
    [InlineData("queryParams")]
    [InlineData("requestContentType")]
    [InlineData("timeout")]
    public async Task Handle_AmbiguousBaseCapabilityProperty_Returns400(string property)
    {
        var thingId = Guid.NewGuid();
        // Two namespaced keys both suffix-match the property — ambiguous, like url/httpMethod/headers.
        var props = $$"""
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "A.{{property}}":      {"Value":"1"},
          "B.{{property}}":      {"Value":"2"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteFindThing(req, thingId, "EP")
            ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain($"ambiguous properties for {property}");
    }

    [Fact]
    public async Task Handle_ConfiguredTimeout_DoesNotBreakSuccessfulCall()
    {
        // A configured timeout resolves and is applied to the outbound client without disrupting a
        // normal call. End-to-end enforcement against a slow endpoint is not asserted here — the
        // factory's handler answers synchronously — though MockHttpMessageHandler.ObservingCancellation
        // now makes that expressible. The TimeSpan mapping is unit-tested in
        // OutboundRequestTests.ResolveTimeout; this pins that a custom timeout flows through /handle.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.timeout":    {"Value":5}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") return Json("{\"ok\":true}");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"ok\":true}");
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

    private static string EmptyProps() => "{}";
}
