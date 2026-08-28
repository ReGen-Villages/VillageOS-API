using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.DagNode;
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

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        fetched.Should().HaveCount(2);
        // The fetch names the registration, never the source — one is a planner's word for a
        // provider, the other is what the fetching service resolves.
        string.Join(" ", fetched).Should()
            .Contain("NationalFloodPortalEndpoint").And.Contain("OpenMeteoEndpoint");
    }

    // What a failed source leaves behind is the runner's own answer, covered in DiscoveryRunnerTests. What
    // this holds is that one provider being down does not stop the sources beside it.
    [Fact]
    public async Task Handle_OneSourceFailing_StillIngestsTheOthers()
    {
        var ids = TwoSourceNames();
        var fetched = new ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                fetched.Add(body);
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

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "one provider being down is not the run failing");
        fetched.Should().HaveCount(2, "a provider that is down does not stop the sources beside it");
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

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
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

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        fetches.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ModelReadFails_WritesNothingRatherThanReportingNoCoverage()
    {
        // The distinction the whole path exists for: an unreachable gateway must not read as "no source
        // covers this site", which is the silently-short list that hides real sources. The run can no
        // longer say so in its answer — it has already given one — so it says it by writing nothing. The
        // site stays in the state that dispatched it, and the run is driven again.
        var fetches = 0;
        var writes = 0;
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.RequestUri!.AbsolutePath == FetchRoute) fetches++;
            if (request.RequestUri.AbsolutePath == "/api/relationships") writes++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = Guid.NewGuid() });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        fetches.Should().Be(0, "a coverage read that failed selected no sources, and no source is not none");
        writes.Should().Be(0, "starting the analysis would claim a discovery that never happened");
    }

    // The same rule one read later: the subject was read as a site, and it is the read of what covers it
    // that fails. Told by the mark the site's read asks for, so that the read failing is the one meant.
    [Fact]
    public async Task Handle_TheSitesCoverageReadFails_WritesNothingRatherThanReportingNoCoverage()
    {
        var ids = TwoSourceNames();
        var coverageReads = 0;
        var fetches = 0;
        var writes = 0;
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/subscriptions"
                && request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()
                    .Contains(CoveringSourceResolver.SiteAnalysisConnectionFlag, StringComparison.Ordinal))
            {
                coverageReads++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            if (request.RequestUri!.AbsolutePath == FetchRoute) fetches++;
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/facts", StringComparison.Ordinal)) writes++;
            return RouteSubscription(request, ids, TwoCoveringSources())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        coverageReads.Should().Be(1);
        fetches.Should().Be(0);
        writes.Should().Be(0, "stamped, the site would read as worked out by a run that read nothing");
    }

    // The body Mycelium posts when a site enters the state the discovery connection watches. Every field
    // beside the subject belongs to the record-edge the dispatch wrote, and a run reads none of them — but
    // a service that refused the whole body over them is a service nothing in the platform can dispatch.
    [Fact]
    public async Task Handle_TheBodyADispatchPosts_RunsTheSiteItNames()
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

        var response = await client.PostAsJsonAsync("/handle", new
        {
            relationshipId = Guid.NewGuid().ToString(),
            subjectId = ids["WillowBend"].ToString(),
            targetId = Guid.NewGuid().ToString(),
            subjectName = "WillowBend",
            targetName = "discoversSite",
            properties = new Dictionary<string, object?> { ["__DispatchState"] = "Pending" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        fetched.Should().HaveCount(2);
        string.Join(" ", fetched).Should().Contain(ids["WillowBend"].ToString());
    }

    [Fact]
    public async Task Handle_ABodyNamingNoSubject_IsRefusedInTheSharedWords()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain(HandleRequestRouter.DescribeExpectedShapes("Forage"));
    }

    // A body the service cannot read at all is still a body it can refuse. Raising instead leaves the
    // caller with a failed request, and a failed dispatch is re-driven — so text that can never parse
    // would be retried on a loop rather than turned down once.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json")]
    [InlineData("{\"subjectId\":")]
    public async Task Handle_ABodyThatIsNotJson_IsRefusedInTheSharedWords(string body)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/handle", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain(HandleRequestRouter.DescribeExpectedShapes("Forage"));
    }

    [Fact]
    public async Task Handle_ReleasesTheSubscriptionItOpened()
    {
        // A read that leaves its subscription live would leak one per discovery run for the life of
        // the process. A run reads more than once — what the subject is, then what it reaches — and
        // every one of those has to be released.
        var ids = Names("WillowBend", "isIn", "covers", "resolvedBy");
        var opened = 0;
        var released = 0;
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/subscriptions")
                opened++;
            if (req.Method == HttpMethod.Delete
                && req.RequestUri!.AbsolutePath.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
                released++;
            return RouteSubscription(req, ids) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        opened.Should().BeGreaterThan(0);
        released.Should().Be(opened);
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
