using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The gateway is Phloem's only path to the broker. The executor tests use a stand-in for it, so
// these cover the real one against a stubbed broker.
public class MyceliumGatewayTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid NodeRunArchetypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PipelineRunArchetypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PredicateId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (MyceliumGateway Gateway, MockHttpMessageHandler Handler) NewGateway(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new MockHttpMessageHandler(respond ?? RespondLikeAModelThatMarksItsArchetypes);
        var gateway = new MyceliumGateway(
            new PerCallHttpClientFactory(handler),
            NullLogger<MyceliumGateway>.Instance,
            MyceliumUrl,
            "svc-token");
        return (gateway, handler);
    }

    // A broker holding a model that marks its run archetypes and answers every predicate name, so the calls
    // under test get past resolution; everything else succeeds.
    private static HttpResponseMessage RespondLikeAModelThatMarksItsArchetypes(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath == "/api/things" && uri.Query.Contains("name="))
        {
            var name = Uri.UnescapeDataString(uri.Query.Split("name=")[1]);
            return Json(HttpStatusCode.OK, $$"""{"id":"{{PredicateId}}","name":"{{name}}"}""");
        }

        if (uri.AbsolutePath == "/api/subscriptions" && request.Method == HttpMethod.Post)
        {
            var selector = request.Content!.ReadAsStringAsync().Result;
            if (selector.Contains(PipelineArchetypes.PipelineRunFlag))
                return MarkedArchetypeSnapshot(PipelineRunArchetypeId, PipelineArchetypes.PipelineRunFlag);
            if (selector.Contains(PipelineArchetypes.NodeRunFlag))
                return MarkedArchetypeSnapshot(NodeRunArchetypeId, PipelineArchetypes.NodeRunFlag);
            return Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""");
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static HttpResponseMessage MarkedArchetypeSnapshot(Guid archetypeId, string roleFlag) =>
        Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            snapshot = new
            {
                things = new[]
                {
                    new
                    {
                        id = archetypeId,
                        name = "whatever this model calls it",
                        properties = new Dictionary<string, object>
                        {
                            [roleFlag] = new { value = true, type = "vos.Boolean" },
                        },
                    },
                },
                relationships = Array.Empty<object>(),
            },
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static IReadOnlyList<HttpRequestMessage> RequestsTo(
        MockHttpMessageHandler handler, string path, HttpMethod? method = null) =>
        handler.Requests
            .Where(request => request.RequestUri!.AbsolutePath == path
                && (method is null || request.Method == method))
            .ToList();

    [Fact]
    public async Task RegisterAsync_DeclaresPhloemToTheBroker()
    {
        var (gateway, handler) = NewGateway();

        var registered = await gateway.RegisterAsync(port: 7300);

        registered.Should().BeTrue();
        var body = await ReadJson(RequestsTo(handler, "/api/mycelium/register").Single());
        body.GetProperty("serviceName").GetString().Should().Be("Phloem");
    }

    [Fact]
    public async Task LoadPipelineSubgraphAsync_AsksForTheWholePipelineClosureInOneSnapshot()
    {
        var pipelineId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadPipelineSubgraphAsync(pipelineId, CancellationToken.None);

        var selector = await ReadJson(RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Single());
        selector.GetProperty("ids")[0].GetString().Should().Be(pipelineId.ToString());
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeTrue();
        // The only names on the selector are the two built-in predicates: an archetype name asked for here
        // is what stopped finding anything the moment a model renamed one (#6516).
        selector.GetProperty("names").EnumerateArray().Select(name => name.GetString())
            .Should().BeEquivalentTo(["is", "has"]);
        selector.TryGetProperty("types", out _).Should().BeFalse();

        selector.GetProperty("markedTypes").EnumerateArray().Select(flag => flag.GetString())
            .Should().BeEquivalentTo([PipelineArchetypes.PortFlag, PipelineArchetypes.PipelineWireFlag]);

        // And the archetype for every role, so one missing from the snapshot means the model marks it
        // nowhere rather than that this pipeline has no node playing it.
        selector.GetProperty("markedArchetypes").EnumerateArray().Select(flag => flag.GetString())
            .Should().BeEquivalentTo(PipelineArchetypes.DagRoleFlags);
    }

    [Fact]
    public async Task LoadPipelineSubgraphAsync_ReturnsTheParsedGraph()
    {
        var pipelineId = Guid.NewGuid();
        var archetypeId = Guid.NewGuid();
        var isPredicateId = Guid.NewGuid();
        var snapshot = $$"""
        {
          "snapshot": {
            "things": [
              { "id": "{{pipelineId}}", "name": "Demo", "properties": {} },
              { "id": "{{archetypeId}}", "name": "Pipeline", "properties": {} },
              { "id": "{{isPredicateId}}", "name": "is", "properties": {} }
            ],
            "relationships": [
              { "id": "{{Guid.NewGuid()}}", "subjectId": "{{pipelineId}}",
                "predicateId": "{{isPredicateId}}", "targetId": "{{archetypeId}}", "properties": {} }
            ]
          }
        }
        """;
        var (gateway, _) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, snapshot)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var graph = await gateway.LoadPipelineSubgraphAsync(pipelineId, CancellationToken.None);

        graph.Things.Should().HaveCount(3);
        graph.Thing(pipelineId)!.Name.Should().Be("Demo");
    }

    // Phloem reads the snapshot once and never streams it, so a subscription left behind is a slice of the
    // model the broker keeps resolving for a reader that has gone.
    [Fact]
    public async Task LoadPipelineSubgraphAsync_ReleasesTheSnapshotSubscription()
    {
        var subscriptionId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions" && request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    subscriptionId,
                    snapshot = new { things = Array.Empty<object>(), relationships = Array.Empty<object>() },
                }))
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadPipelineSubgraphAsync(Guid.NewGuid(), CancellationToken.None);

        // The release is deliberately not awaited — the caller gets its graph without waiting on cleanup.
        var released = await EventuallyAsync(() =>
            RequestsTo(handler, $"/api/subscriptions/{subscriptionId}", HttpMethod.Delete).Count == 1);
        released.Should().BeTrue();
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20);
        return condition();
    }

    [Fact]
    public async Task LoadPipelineSubgraphAsync_WhenTheBrokerRefuses_SurfacesTheFailure()
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var loading = () => gateway.LoadPipelineSubgraphAsync(Guid.NewGuid(), CancellationToken.None);

        await loading.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateRunAsync_RecordsTheRunAsStartedAndTiesItToItsPipeline()
    {
        var runId = Guid.NewGuid();
        var pipelineId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        var created = await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single());
        created.GetProperty("id").GetString().Should().Be(runId.ToString());
        created.GetProperty("properties").GetProperty("status").GetString().Should().Be(RunStatus.Running);
        created.GetProperty("properties").GetProperty("pipelineId").GetString().Should().Be(pipelineId.ToString());

        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().HaveCount(2);
    }

    [Fact]
    public async Task SetNodeRunStatusAsync_FirstTime_CreatesTheNodeRunAndAttachesItToTheRun()
    {
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Echo", RunStatus.Running, error: null, CancellationToken.None);

        var created = await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single());
        created.GetProperty("name").GetString().Should().Be("NodeRun Echo");
        created.GetProperty("properties").GetProperty("status").GetString().Should().Be(RunStatus.Running);
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().HaveCount(2);
    }

    // The animation in the browser watches one Thing change status; a second create would show as a
    // separate node rather than the same one moving on.
    [Fact]
    public async Task SetNodeRunStatusAsync_SecondTime_ChangesTheSameNodeRunRatherThanCreatingAnother()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Succeeded, null, CancellationToken.None);

        RequestsTo(handler, "/api/things", HttpMethod.Post).Should().HaveCount(1);
        var propertyWrites = handler.Requests.Where(request =>
            request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/properties")).ToList();
        propertyWrites.Should().HaveCount(1);
        (await ReadJson(propertyWrites.Single())).GetProperty("value").GetString()
            .Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task SetNodeRunStatusAsync_WhenItFails_RecordsTheErrorAlongsideTheStatus()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Failed, "boom", CancellationToken.None);

        var written = new List<string?>();
        foreach (var request in handler.Requests.Where(request => request.Method == HttpMethod.Put))
            written.Add((await ReadJson(request)).GetProperty("name").GetString());

        written.Should().Contain("status").And.Contain("error");
    }

    // A fan-out gives each item its own NodeRun, so the browser shows one per item rather than one
    // that keeps changing.
    [Fact]
    public async Task SetNodeRunStatusAsync_ForAFanOut_KeepsEachItemSeparate()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None, index: 0, total: 2);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None, index: 1, total: 2);

        var created = RequestsTo(handler, "/api/things", HttpMethod.Post);
        created.Should().HaveCount(2);
        var names = new List<string?>();
        foreach (var request in created)
            names.Add((await ReadJson(request)).GetProperty("name").GetString());
        names.Should().BeEquivalentTo(["NodeRun Echo #0", "NodeRun Echo #1"]);
    }

    [Fact]
    public async Task SetRunStatusAsync_WritesTheStatusOnTheRun()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);

        var request = RequestsTo(handler, $"/api/things/{runId}/properties", HttpMethod.Put).Single();
        var body = await ReadJson(request);
        body.GetProperty("name").GetString().Should().Be("status");
        body.GetProperty("value").GetString().Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task SetRunResultAsync_PublishesThePipelineResultOnTheRun()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        var result = JsonDocument.Parse("""{"total":3}""").RootElement;

        await gateway.SetRunResultAsync(runId, result, CancellationToken.None);

        var body = await ReadJson(RequestsTo(handler, $"/api/things/{runId}/properties", HttpMethod.Put).Single());
        body.GetProperty("name").GetString().Should().Be("result");
        body.GetProperty("value").GetString().Should().Contain("\"total\":3");
    }

    [Theory]
    [InlineData("""{"properties":{"cancelRequested":{"value":true,"type":"vos.Boolean"}}}""", true)]
    [InlineData("""{"properties":{"cancelRequested":{"value":"true","type":"vos.String"}}}""", true)]
    [InlineData("""{"properties":{"cancelRequested":{"value":"TRUE","type":"vos.String"}}}""", true)]
    [InlineData("""{"properties":{"cancelRequested":true}}""", true)]
    [InlineData("""{"properties":{"cancelRequested":{"value":false,"type":"vos.Boolean"}}}""", false)]
    [InlineData("""{"properties":{"cancelRequested":{"value":"no","type":"vos.String"}}}""", false)]
    [InlineData("""{"properties":{}}""", false)]
    [InlineData("""{}""", false)]
    public async Task IsCancelRequestedAsync_ReadsTheFlagThroughItsPropertyEnvelope(string thingJson, bool expected)
    {
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.OK, thingJson));

        var cancelRequested = await gateway.IsCancelRequestedAsync(Guid.NewGuid(), CancellationToken.None);

        cancelRequested.Should().Be(expected);
    }

    // A run whose Thing cannot be read is not a cancelled run; cancelling on a read failure would
    // stop pipelines whenever the broker hiccups.
    [Fact]
    public async Task IsCancelRequestedAsync_WhenTheRunCannotBeRead_DoesNotReportCancellation()
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await gateway.IsCancelRequestedAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_SendsTheEnvelopeToTheNamedEndpointAndReturnsWhatItSaid()
    {
        var (gateway, handler) = NewGateway(_ => Json(HttpStatusCode.Accepted, """{"outputs":{"sum":3}}"""));
        var envelope = JsonDocument.Parse("""{"runId":"r","nodeId":"n"}""").RootElement;

        var result = await gateway.DispatchAsync("energy balance", envelope, CancellationToken.None);

        result.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        result.Body.Should().Contain("\"sum\":3");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/endpoints/energy%20balance");
    }

    [Fact]
    public async Task DispatchAsync_WhenTheEndpointFails_ReportsTheStatusRatherThanThrowing()
    {
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.BadGateway, "upstream is down"));
        var envelope = JsonDocument.Parse("{}").RootElement;

        var result = await gateway.DispatchAsync("echo", envelope, CancellationToken.None);

        result.StatusCode.Should().Be((int)HttpStatusCode.BadGateway);
        result.Body.Should().Be("upstream is down");
    }

    // An archetype cannot change role while the process lives, so asking the broker twice is wasted work —
    // and this runs once per node run.
    [Fact]
    public async Task TheGatewayResolvesARoleArchetypeOnceAndReusesIt()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(runId, Guid.NewGuid(), "First", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, Guid.NewGuid(), "Second", RunStatus.Running, null, CancellationToken.None);

        var nodeRunLookups = 0;
        foreach (var request in RequestsTo(handler, "/api/subscriptions", HttpMethod.Post))
            if ((await request.Content!.ReadAsStringAsync()).Contains(PipelineArchetypes.NodeRunFlag))
                nodeRunLookups++;
        nodeRunLookups.Should().Be(1);
    }

    // The run archetype is asked for on its own. Through markedTypes the answer would have carried every
    // Thing that already is one — every run the model has ever recorded.
    [Fact]
    public async Task ResolvingARoleArchetype_AsksForTheArchetypeWithoutItsMembers()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var lookup = await ReadJson(RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Single());
        lookup.GetProperty("markedArchetypes").EnumerateArray().Select(flag => flag.GetString())
            .Should().BeEquivalentTo([PipelineArchetypes.PipelineRunFlag]);
        lookup.TryGetProperty("markedTypes", out _).Should().BeFalse();
        lookup.GetProperty("includeIsAncestors").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheModelMarksNoRunArchetype_TheFailureNamesTheFlag()
    {
        var (gateway, _) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var creating = () => gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await creating.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{PipelineArchetypes.PipelineRunFlag}*");
    }

    private static async Task<JsonElement> ReadJson(HttpRequestMessage request) =>
        JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync());
}
