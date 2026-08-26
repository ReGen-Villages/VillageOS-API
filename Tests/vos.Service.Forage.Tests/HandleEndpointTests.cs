using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// The /handle surface in vos.Service.Forage/Program.cs, driven end to end: read the model, call
// each covering source through the fetching service, report both halves. The coverage rules are
// pinned in Helpers/CoveringSourceResolverTests and the run's own behaviour in
// Services/DiscoveryRunnerTests; these cover what the endpoint composes from them.
//
// A run resolves its sources in parallel, so the handler callback below is entered on several threads
// at once. Anything a test collects from it has to be a concurrent collection: a plain List loses an
// entry often enough to red a build about once in six runs, and it reds as a wrong count rather than
// as a race.
public class HandleEndpointTests
{
    // The subdomain the run forwards a fetch to, left at its default by the test host.
    private const string FetchRoute = "/api/endpoints/tributary";

    private static Dictionary<string, Guid> Names(params string[] names) =>
        names.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Edge[] TwoCoveringSources() =>
    [
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("Portugal", "isIn", "Earth"),
        new Edge("NationalFloodPortal", "covers", "Portugal"),
        new Edge("NationalFloodPortal", "resolvedBy", "NationalFloodPortalEndpoint"),
        new Edge("OpenMeteo", "covers", "Earth"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
    ];

    private static Dictionary<string, Guid> TwoSourceNames() => Names(
        "WillowBend", "Portugal", "Earth", "isIn", "covers", "resolvedBy",
        "NationalFloodPortal", "NationalFloodPortalEndpoint", "OpenMeteo", "OpenMeteoEndpoint");

    [Fact]
    public async Task Handle_CallsEveryCoveringSource_AndReportsThemResolved()
    {
        var ids = TwoSourceNames();
        var fetched = new ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                fetched.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return Ok("""{"success":true}""");
            }
            return RouteSubscription(req, ids, TwoCoveringSources())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("NationalFloodPortal").And.Contain("OpenMeteo");
        body.Should().Contain("\"unresolved\":[]");
        fetched.Should().HaveCount(2);
        // The fetch names the registration, never the source — one is a planner's word for a
        // provider, the other is what the fetching service resolves.
        string.Join(" ", fetched).Should()
            .Contain("NationalFloodPortalEndpoint").And.Contain("OpenMeteoEndpoint");
    }

    [Fact]
    public async Task Handle_OneSourceFailing_StillIngestsTheOthersAndReportsTheReason()
    {
        var ids = TwoSourceNames();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return body.Contains("NationalFloodPortalEndpoint")
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("portal is down for maintenance", Encoding.UTF8, "text/plain")
                    }
                    : Ok("""{"success":true}""");
            }
            return RouteSubscription(req, ids, TwoCoveringSources())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "one provider being down is not the run failing");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"resolved\":[\"OpenMeteo\"]");
        body.Should().Contain("NationalFloodPortal").And.Contain("503");
        body.Should().Contain("portal is down for maintenance", "the provider's own words are the most useful reason");
    }

    [Fact]
    public async Task Handle_PassesTheSitesOwnValuesToEverySource()
    {
        var ids = TwoSourceNames();
        var bodies = new ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                bodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return Ok("""{"success":true}""");
            }
            return RouteSubscription(req, ids, TwoCoveringSources(),
                       siteValues: new Dictionary<string, string> { ["lat"] = "-25.75", ["lng"] = "28.19" })
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        bodies.Should().HaveCount(2);
        bodies.Should().OnlyContain(body => body.Contains("-25.75") && body.Contains("28.19"));
    }

    [Fact]
    public async Task Handle_SiteNoSourceCovers_ReportsBothHalvesEmptyRatherThanAnError()
    {
        // Covered by nothing is a real answer about the model, not a failure.
        var ids = Names("WillowBend", "Portugal", "isIn", "covers", "resolvedBy");
        var fetches = 0;
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute) { fetches++; return Ok("{}"); }
            return RouteSubscription(req, ids, new Edge("WillowBend", "isIn", "Portugal"))
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"resolved\":[]").And.Contain("\"unresolved\":[]");
        fetches.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ModelReadFails_Returns502RatherThanReportingNoCoverage()
    {
        // The distinction the whole path exists for: an unreachable gateway must not read as "no
        // source covers this site", which is the silently-short list that hides real sources.
        await using var factory = new ForageWebApplicationFactory();
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
        await using var factory = new ForageWebApplicationFactory();
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
        await using var factory = new ForageWebApplicationFactory();
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
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"").And.Contain("\"service\":\"Forage\"");
    }

    [Fact]
    public async Task Shutdown_Returns200WithMessage()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Shutting down Forage");
    }
}
