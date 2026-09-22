using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// Integration tests for the ModelAsset kind through /handle: an endpoint reaching
// ModelAsset through keepsBy deposits the bytes a real upstream fetch served and writes the
// ticket onto the assetSubject's assetProperty as an ordinary observation — timestamped by
// the observedAtParameter value when the model names one, else left to the broker's model
// clock. A cache hit deposits nothing: no new retrieval happened. Requirements are gated up
// front, an unknown keeping kind is refused naming both halves, and a keep the broker
// refuses fails the call rather than quietly keeping nothing.
public class ModelAssetHandleTests : IDisposable
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0x01 };
    private const string Ticket = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "vos-cache-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private static Kind ModelAsset() => new(EndpointKindRoles.Keeping, "ModelAsset", "assetProperty", "assetSubject");
    private static Kind Binary() => new(EndpointKindRoles.ResponseBody, "BinaryResponse");
    private static Kind DiskCache() => new(EndpointKindRoles.Caching, "DiskCache", "cacheTtl");

    [Fact]
    public async Task Handle_ModelAssetWithBinary_DepositsTheBytesAndWritesTheTicket()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return MyceliumStub.Binary(PngBytes, "image/png");
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        envelope.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        Convert.FromBase64String(envelope.RootElement.GetProperty("dataBase64").GetString()!).Should().Equal(PngBytes);
        deposits.Should().HaveCount(1, "a real fetch deposits its bytes exactly once");
        deposits[0].Bytes.Should().Equal(PngBytes);
        deposits[0].ContentType.Should().Be("image/png");
        tickets.Should().HaveCount(1);
        tickets[0].Should().Contain("surfaceMap").And.Contain(Ticket);
        tickets[0].Should().NotContain("observedAt", "with no depicted time the broker stamps model time");
    }

    [Fact]
    public async Task Handle_ModelAssetPlainBody_DepositsTheServedBytes()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"sourceDocument"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test") return Json("""{"n":1}""");
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"n":1}""");
        deposits.Should().HaveCount(1);
        deposits[0].Bytes.Should().Equal(Encoding.UTF8.GetBytes("""{"n":1}"""));
        deposits[0].ContentType.Should().Contain("application/json");
        tickets.Should().HaveCount(1);
        tickets[0].Should().Contain("sourceDocument").And.Contain(Ticket);
    }

    [Fact]
    public async Task Handle_ModelAssetWithDiskCache_ACacheHitDepositsNothing()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.cacheTtl":      {"Value":"300"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var upstreamCalls = 0;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory { CacheDirectory = _cacheDir };
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { upstreamCalls++; return MyceliumStub.Binary(PngBytes, "image/png"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), DiskCache(), ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });
        var second = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        upstreamCalls.Should().Be(1);
        deposits.Should().HaveCount(1, "a cache hit is not a retrieval, so it deposits nothing");
        tickets.Should().HaveCount(1);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Handle_ModelAsset_ObservedAtParameterTimestampsTheTicket()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":                 {"Value":"https://tiles.test/at/{when}"},
          "Endpoint.httpMethod":          {"Value":"GET"},
          "Endpoint.assetProperty":       {"Value":"surfaceMap"},
          "Endpoint.assetSubject":        {"Value":"SiteAlpha"},
          "Endpoint.observedAtParameter": {"Value":"when"}
        }
        """;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return MyceliumStub.Binary(PngBytes, "image/png");
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["when"] = "1998-06-15T00:00:00Z" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        deposits.Should().HaveCount(1);
        tickets.Should().HaveCount(1);
        tickets[0].Should().Contain("observedAt").And.Contain("1998-06-15",
            "the value that selected the historical image also timestamps it");
    }

    [Fact]
    public async Task Handle_ModelAsset_ObservedAtValueThatIsNotATime_RefusedBeforeTheFetch()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":                 {"Value":"https://tiles.test/at/{when}"},
          "Endpoint.httpMethod":          {"Value":"GET"},
          "Endpoint.assetProperty":       {"Value":"surfaceMap"},
          "Endpoint.assetSubject":        {"Value":"SiteAlpha"},
          "Endpoint.observedAtParameter": {"Value":"when"}
        }
        """;
        var upstreamCalls = 0;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") { upstreamCalls++; return MyceliumStub.Binary(PngBytes, "image/png"); }
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), ModelAsset())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["when"] = "latest" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("observedAtParameter").And.Contain("latest");
        upstreamCalls.Should().Be(0, "a ticket that cannot be timestamped honestly is refused before any bytes are fetched");
    }

    [Fact]
    public async Task Handle_ModelAsset_MissingAssetSubject_RefusedByTheRequirementsGate()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, ModelAsset())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ModelAsset").And.Contain("assetSubject");
    }

    [Fact]
    public async Task Handle_UnknownKeepingKind_RefusedNamingBothHalves()
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
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, new Kind(EndpointKindRoles.Keeping, "S3Vault"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("S3Vault").And.Contain("ModelAsset");
    }

    [Fact]
    public async Task Handle_ModelAssetWithOffsetPaging_Refused()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"sourceDocument"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"},
          "Endpoint.offsetParam":   {"Value":"offset"},
          "Endpoint.hasMorePath":   {"Value":"more"},
          "Endpoint.itemsPath":     {"Value":"items"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, ModelAsset(),
                    new Kind(EndpointKindRoles.Paging, "OffsetPaging", "offsetParam", "hasMorePath", "itemsPath"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ModelAsset").And.Contain("OffsetPaging");
    }

    [Fact]
    public async Task Handle_ModelAsset_DepositRefused_FailsTheCallAndWritesNoTicket()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return MyceliumStub.Binary(PngBytes, "image/png");
            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath == "/api/assets")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), ModelAsset())
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway,
            "an endpoint that promises to keep and cannot must not answer as though it kept");
        tickets.Should().BeEmpty("a ticket must never claim bytes the store did not accept");
    }

    [Fact]
    public async Task Handle_ModelAsset_SubjectNowhereInTheModel_FailsTheCall()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "tiles.test") return MyceliumStub.Binary(PngBytes, "image/png");
            return RouteFindThing(req, thingId, "EP") ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, Binary(), ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SiteAlpha");
    }

    [Fact]
    public async Task Handle_ModelAsset_ProviderAnswers404_DepositsNothing()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://api.test/data"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"sourceDocument"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        var deposits = new List<(byte[] Bytes, string? ContentType)>();
        var tickets = new List<string>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.Host == "api.test")
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent("""{"held":"nothing"}""", Encoding.UTF8, "application/json") };
            return RouteFindThing(req, thingId, "EP") ?? RouteFindThing(req, subjectId, "SiteAlpha")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKinds(req, thingId, ModelAsset())
                ?? RouteAssetDeposit(req, Ticket, deposits)
                ?? RouteObservationWrite(req, subjectId, tickets)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "404 is the provider's complete answer, not a refusal");
        deposits.Should().BeEmpty("the provider served no content to keep");
        tickets.Should().BeEmpty();
    }
}
