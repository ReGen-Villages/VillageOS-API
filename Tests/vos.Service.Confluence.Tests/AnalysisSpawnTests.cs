using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Confluence.Tests.ModelSnapshotStub;

namespace vos.Service.Confluence.Tests;

// What happens once the run finishes: the site's analysis starts, whatever mixture of sources
// resolved. Driven through /handle, because the thing worth pinning is that discovery and the
// analysis are one path a caller triggers rather than two a caller has to sequence.
//
// The analysis is started by writing `Site runs Pipeline`, never by calling the orchestrator. `runs`
// is a handled predicate, so the edge is the trigger — and every test here asserts on the
// relationship written rather than on any call to Phloem, because a second way to start an analysis
// is what these are meant to keep from appearing.
public class AnalysisSpawnTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    private static Dictionary<string, Guid> Names(params string[] names) =>
        names.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, Guid> SiteWithOneSource() => Names(
        "WillowBend", "Portugal", "isIn", "covers", "resolvedBy", "analysedBy",
        "OpenMeteo", "OpenMeteoEndpoint", "SiteAnalysis", "runs");

    private static Edge[] OneSourceAndAPipeline() =>
    [
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("OpenMeteo", "covers", "Portugal"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
        new Edge("WillowBend", "analysedBy", "SiteAnalysis"),
    ];

    // Records the relationship the run writes, and answers the predicate lookup that precedes it.
    private sealed class SpawnRecorder
    {
        private readonly Dictionary<string, Guid> _ids;
        public List<(Guid Subject, Guid Predicate, Guid Target)> Written { get; } = new();

        public SpawnRecorder(Dictionary<string, Guid> ids) => _ids = ids;

        public HttpResponseMessage? Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/api/things"
                && request.RequestUri.Query.Contains("name=runs"))
                return Ok("{\"Id\":\"" + _ids["runs"] + "\",\"Name\":\"runs\",\"Properties\":{}}");

            if (request.Method == HttpMethod.Post && path == "/api/relationships")
            {
                var body = JsonDocument.Parse(
                    request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
                Written.Add((
                    body.GetProperty("subjectId").GetGuid(),
                    body.GetProperty("predicateId").GetGuid(),
                    body.GetProperty("targetId").GetGuid()));
                return Ok("{}");
            }

            return null;
        }
    }

    [Fact]
    public async Task Handle_EverySourceResolved_RelatesTheSiteToItsPipeline()
    {
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder(ids);
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            spawns.Route(req)
            ?? (req.RequestUri!.AbsolutePath == FetchRoute ? Ok("""{"success":true}""") : null)
            ?? RouteSubscription(req, ids, OneSourceAndAPipeline())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"started\":true");
        spawns.Written.Should().ContainSingle()
            .Which.Should().Be((ids["WillowBend"], ids["runs"], ids["SiteAnalysis"]));
    }

    [Fact]
    public async Task Handle_EverySourceFailed_StartsTheAnalysisAnyway()
    {
        // The case the analysis most needs to run for: a planner has to be told the balances could
        // not be computed and why, which cannot happen if nothing ever runs.
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder(ids);
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            spawns.Route(req)
            ?? (req.RequestUri!.AbsolutePath == FetchRoute
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                    { Content = new StringContent("portal down", Encoding.UTF8, "text/plain") }
                : null)
            ?? RouteSubscription(req, ids, OneSourceAndAPipeline())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"resolved\":[]");
        body.Should().Contain("\"started\":true");
        spawns.Written.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoSourceCoversTheSite_StartsTheAnalysisAnyway()
    {
        var ids = Names("WillowBend", "Portugal", "isIn", "analysedBy", "SiteAnalysis", "runs");
        var spawns = new SpawnRecorder(ids);
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            spawns.Route(req)
            ?? RouteSubscription(req, ids,
                [new Edge("WillowBend", "isIn", "Portugal"),
                 new Edge("WillowBend", "analysedBy", "SiteAnalysis")])
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        spawns.Written.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SiteAnalysedByNoPipeline_StartsNothingAndSaysWhy()
    {
        var ids = Names("WillowBend", "Portugal", "isIn", "covers", "resolvedBy",
            "OpenMeteo", "OpenMeteoEndpoint", "runs");
        var spawns = new SpawnRecorder(ids);
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
            spawns.Route(req)
            ?? (req.RequestUri!.AbsolutePath == FetchRoute ? Ok("""{"success":true}""") : null)
            ?? RouteSubscription(req, ids,
                [new Edge("WillowBend", "isIn", "Portugal"),
                 new Edge("OpenMeteo", "covers", "Portugal"),
                 new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint")])
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo");
        body.Should().Contain("\"started\":false").And.Contain("analysed by no pipeline");
        spawns.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StartingTheAnalysisFails_StillReportsWhatDiscoveryFound()
    {
        // Discovery's observations are already written by the time the analysis is asked for. Losing
        // the report because the pipeline could not be started would throw away work that succeeded.
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/relationships")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                return Ok("{\"Id\":\"" + ids["runs"] + "\",\"Name\":\"runs\",\"Properties\":{}}");
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"unresolved\":[]");
        body.Should().Contain("\"started\":false");
    }

    [Fact]
    public async Task Handle_PredicateAnsweredWithoutAnIdentifier_ReportsItRatherThanFailingTheRun()
    {
        // A Thing answered without an id cannot be related to, and reading one field off a body that
        // does not carry it is how a wrong identifier gets used instead of none.
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                return Ok("""{"Name":"runs","Properties":{}}""");
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("\"started\":false").And.Contain("'runs' predicate");
    }

    [Fact]
    public async Task Handle_PredicateLookupRefused_ReportsItRatherThanFailingTheRun()
    {
        // A refused lookup is not proof the predicate is absent, but the run cannot start an analysis
        // without it either way, and discovery's own result must survive being unable to.
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"unresolved\":[]").And.Contain("\"started\":false");
    }

    [Fact]
    public async Task Handle_PredicateLookupCannotBeReached_ReportsItRatherThanFailingTheRun()
    {
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                throw new HttpRequestException("the model is unreachable");
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"started\":false");
    }

    [Fact]
    public async Task Handle_RelationshipWriteCannotBeReached_ReportsItRatherThanFailingTheRun()
    {
        // The write is the last thing a run does, and it reaches the network. An exception raised
        // there must not escape as a 500 that throws away observations already written.
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/api/relationships")
                throw new HttpRequestException("the model is unreachable");
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                return Ok("{\"Id\":\"" + ids["runs"] + "\",\"Name\":\"runs\",\"Properties\":{}}");
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"started\":false");
    }

    [Fact]
    public async Task Handle_ModelHasNoRunsPredicate_ReportsItRatherThanFailingTheRun()
    {
        var ids = SiteWithOneSource();
        await using var factory = new ConfluenceWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/things")
                return Ok("null");
            if (req.RequestUri!.AbsolutePath == FetchRoute) return Ok("""{"success":true}""");
            return RouteSubscription(req, ids, OneSourceAndAPipeline())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("\"started\":false").And.Contain("'runs' predicate");
    }
}
