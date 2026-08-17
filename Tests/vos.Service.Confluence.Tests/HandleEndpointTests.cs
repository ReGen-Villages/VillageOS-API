using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Confluence.Tests.ModelSnapshotStub;

namespace vos.Service.Confluence.Tests;

// The /handle surface in vos.Service.Confluence/Program.cs. The coverage rules themselves are pinned
// in Helpers/CoveringSourceResolverTests; these cover what the endpoint does with them, including the
// two failures a caller cannot tell apart from the answer alone.
public class HandleEndpointTests
{
    private static Dictionary<string, Guid> Names(params string[] names) =>
        names.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    [Fact]
    public async Task Handle_ReturnsEverySourceCoveringTheSite()
    {
        var ids = Names("WillowBend", "Portugal", "Earth", "isIn", "covers", "resolvedBy",
            "NationalFloodPortal", "NationalFloodPortalEndpoint", "OpenMeteo", "OpenMeteoEndpoint");
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteSubscription(req, ids,
            new Edge("WillowBend", "isIn", "Portugal"),
            new Edge("Portugal", "isIn", "Earth"),
            new Edge("NationalFloodPortal", "covers", "Portugal"),
            new Edge("NationalFloodPortal", "resolvedBy", "NationalFloodPortalEndpoint"),
            new Edge("OpenMeteo", "covers", "Earth"),
            new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"))
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("NationalFloodPortal").And.Contain("OpenMeteo");
        body.Should().Contain("NationalFloodPortalEndpoint");
    }

    [Fact]
    public async Task Handle_SiteNoSourceCovers_ReturnsAnEmptyListRatherThanAnError()
    {
        // Covered by nothing is a real answer about the model, not a failure.
        var ids = Names("WillowBend", "Portugal", "isIn", "covers", "resolvedBy");
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req => RouteSubscription(req, ids,
            new Edge("WillowBend", "isIn", "Portugal"))
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"covering\":[]");
    }

    [Fact]
    public async Task Handle_ModelReadFails_Returns502RatherThanReportingNoCoverage()
    {
        // The distinction the whole path exists for: an unreachable gateway must not read as "no
        // source covers this site", which is the silently-short list that hides real sources.
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Refusing rather than reporting that none do");
    }

    [Fact]
    public async Task Handle_MissingSiteId_Returns400()
    {
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("siteId");
    }

    [Fact]
    public async Task Handle_ReleasesTheSubscriptionItOpened()
    {
        // A read that leaves its subscription live would leak one per discovery run for the life of
        // the process.
        var ids = Names("WillowBend", "isIn", "covers", "resolvedBy");
        var released = 0;
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Delete
                && req.RequestUri!.AbsolutePath.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
                released++;
            return RouteSubscription(req, ids) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        released.Should().Be(1);
    }

    [Fact]
    public async Task Health_ReportsTheService()
    {
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"").And.Contain("\"service\":\"Confluence\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shutting down Confluence");
    }
}
