using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Integration tests for the generic token-exchange + offset-paging handling in /handle (Task #5470),
// exercised through an ESRI-shaped configuration (and one header-attach variant to show the code
// carries no source-specific assumptions). The mycelium calls, the token-endpoint call, and the
// outbound data call all resolve through one MockHttpMessageHandler set per scenario.
//
// Effective-property keys are namespaced (e.g. "Esri.tokenUrl") to mirror Mycelium's template-merged
// shape; EffectivePropertyResolver suffix-matches them to the lookups the handler performs.
public class EsriHandleTests
{
    [Fact]
    public async Task Handle_NoAuthKind_AttachesNoTokenAndReturnsBody()
    {
        // No-auth smoke path: an endpoint without an authKind behaves as a plain REST call.
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://sampleserver6.arcgisonline.com/arcgis/rest/services/USA/MapServer/0/query"},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "sampleserver6.arcgisonline.com") { outbound = req; return Json("{\"features\":[]}"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.RequestUri!.Query.Should().NotContain("token=");
    }

    [Fact]
    public async Task Handle_TokenExchange_FetchesTokenAndAttachesAsQueryParam()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"},
          "Esri.tokenUrl":       {"Value":"https://tokens.test/generateToken"},
          "Esri.tokenRequest":   {"Value":{"username":"alice","password":"s3cret","f":"json"}},
          "Esri.tokenPath":      {"Value":"token"},
          "Esri.expiryPath":     {"Value":"expires"},
          "Esri.expiryUnit":     {"Value":"epochMillis"}
        }
        """;
        HttpRequestMessage? outbound = null;
        var tokenCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tokens.test")
            {
                tokenCalls++;
                return Json($$"""{"token":"FETCHED","expires":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()}}}""");
            }
            if (req.RequestUri.Host == "features.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tokenCalls.Should().Be(1);
        outbound!.RequestUri!.Query.Should().Contain("token=FETCHED");
    }

    [Fact]
    public async Task Handle_TokenExchange_PreMintedToken_SkipsMint()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"},
          "Esri.tokenUrl":       {"Value":"https://tokens.test/generateToken"},
          "Esri.token":          {"Value":"PRE-MINTED"}
        }
        """;
        HttpRequestMessage? outbound = null;
        var tokenCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tokens.test") { tokenCalls++; return Json("{\"token\":\"NOPE\",\"expires\":0}"); }
            if (req.RequestUri.Host == "features.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tokenCalls.Should().Be(0, "a pre-minted token short-circuits the mint");
        outbound!.RequestUri!.Query.Should().Contain("token=PRE-MINTED");
    }

    [Fact]
    public async Task Handle_TokenExchange_HeaderAttach_AddsBearerAuthorizationHeader()
    {
        // tokenHeader/tokenScheme move the credential off the query string and into a header — the
        // attach point is config, so the same code serves bearer-header auth (e.g. OAuth2).
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"},
          "Esri.token":          {"Value":"PRE-MINTED"},
          "Esri.tokenHeader":    {"Value":"Authorization"},
          "Esri.tokenScheme":    {"Value":"Bearer"}
        }
        """;
        HttpRequestMessage? outbound = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "features.test") { outbound = req; return Json("{\"ok\":true}"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        outbound!.RequestUri!.Query.Should().NotContain("token=");
        outbound.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer PRE-MINTED");
    }

    [Fact]
    public async Task Handle_TokenExchange_MissingMintConfig_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"}
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
        (await response.Content.ReadAsStringAsync()).Should().Contain("tokenUrl");
    }

    [Fact]
    public async Task Handle_TokenExchange_TokenEndpointRejects_Returns502WithoutLeakingMessage()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"},
          "Esri.tokenUrl":       {"Value":"https://tokens.test/generateToken"},
          "Esri.tokenRequest":   {"Value":{"username":"alice","password":"wrong"}},
          "Esri.tokenPath":      {"Value":"token"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tokens.test")
                return Json("""{"error":{"code":400,"message":"Invalid username or password."}}""");
            if (req.RequestUri.Host == "features.test")
                return Json("{\"ok\":true}");
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Invalid username or password");
        body.Should().Contain("authentication token");
    }

    [Fact]
    public async Task Handle_OffsetPaging_AggregatesAllPagesBeforeTransform()
    {
        // pageSize=2: offset 0 returns [1,2] with exceededTransferLimit=true; offset 2 returns [3] not
        // exceeded. The reshape counts items onto the site, so correct aggregation observes 3 — a
        // single (non-paginated) fetch would see only the first page (2).
        var thingId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://features.test/query"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"name\": \"ExampleSite\", \"properties\": {\"featureCount\": $count(features)}}"},
          "Esri.pagingKind":            {"Value":"offset"},
          "Esri.offsetParam":           {"Value":"resultOffset"},
          "Esri.pageSizeParam":         {"Value":"resultRecordCount"},
          "Esri.pageSize":              {"Value":"2"},
          "Esri.hasMorePath":           {"Value":"exceededTransferLimit"},
          "Esri.itemsPath":             {"Value":"features"}
        }
        """;
        string? observations = null;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "features.test")
            {
                return req.RequestUri.Query.Contains("resultOffset=2")
                    ? Json("{\"features\":[{\"attributes\":{\"OBJECTID\":3}}],\"exceededTransferLimit\":false}")
                    : Json("{\"features\":[{\"attributes\":{\"OBJECTID\":1}},{\"attributes\":{\"OBJECTID\":2}}],\"exceededTransferLimit\":true}");
            }
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath == $"/api/things/{siteId}/observations")
            {
                observations = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json("""{"accepted":1}""");
            }
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
        observations.Should().NotBeNull();
        observations!.Should().Contain("\"property\":\"featureCount\"").And.Contain("\"value\":3");
    }

    // 404 is here beside a refusal because it is the one status the two paths read differently:
    // a single call takes it for an answer, a page cannot.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "this caller may not read past the first page")]
    [InlineData(HttpStatusCode.NotFound, "there is no page at that offset")]
    public async Task Handle_OffsetPaging_PageOutside2xx_AnswersWithItsStatusAndWords(
        HttpStatusCode refused, string providerWords)
    {
        var thingId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://features.test/query"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"name\": \"ExampleSite\", \"properties\": {\"featureCount\": $count(features)}}"},
          "Esri.pagingKind":            {"Value":"offset"},
          "Esri.offsetParam":           {"Value":"resultOffset"},
          "Esri.pageSizeParam":         {"Value":"resultRecordCount"},
          "Esri.pageSize":              {"Value":"2"},
          "Esri.hasMorePath":           {"Value":"exceededTransferLimit"},
          "Esri.itemsPath":             {"Value":"features"}
        }
        """;
        var ingested = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "features.test")
            {
                return req.RequestUri.Query.Contains("resultOffset=2")
                    ? new HttpResponseMessage(refused)
                    {
                        Content = new StringContent(providerWords, Encoding.UTF8, "text/plain")
                    }
                    : Json("{\"features\":[{\"attributes\":{\"OBJECTID\":1}},{\"attributes\":{\"OBJECTID\":2}}],\"exceededTransferLimit\":true}");
            }
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath == $"/api/things/{siteId}/observations")
            {
                ingested++;
                return Json("""{"accepted":1}""");
            }
            return RouteFindThing(req, thingId, "EP")
                ?? RouteFindThing(req, siteId, "ExampleSite")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(refused,
            "a walk answered 502 tells a run the fetcher failed, when the provider is the one that said no");
        (await response.Content.ReadAsStringAsync()).Should().Be(providerWords);
        ingested.Should().Be(0, "the pages that did arrive are not a complete answer to reshape");
    }

}
