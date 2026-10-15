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
    private static readonly Guid RunSubjectPredicateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PredicateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SentMessageArchetypeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ProducedPredicateId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ResumptionConnectionId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static (MyceliumGateway Gateway, MockHttpMessageHandler Handler) NewGateway(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new MockHttpMessageHandler(respond ?? new BrokerApplyingItsWriteRules().Respond);
        var gateway = new MyceliumGateway(
            new PerCallHttpClientFactory(handler),
            NullLogger<MyceliumGateway>.Instance,
            MyceliumUrl,
            "svc-token");
        return (gateway, handler);
    }

    // A broker holding a model that binds its run roles and answers every predicate name, so the calls
    // under test get past resolution; everything else succeeds. A Thing is answered PascalCase, which is the
    // naming policy the broker shares with the seed files and not the camelCase of the bodies sent to it.
    private static HttpResponseMessage RespondLikeAModelThatBindsItsRoles(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath == "/api/things" && uri.Query.Contains("name="))
        {
            var name = Uri.UnescapeDataString(uri.Query.Split("name=")[1]);
            return Json(HttpStatusCode.OK,
                $$$"""{"Id":"{{{PredicateId}}}","Name":"{{{name}}}","IsArchetype":false,"Properties":{}}""");
        }

        if (uri.AbsolutePath == "/api/subscriptions" && request.Method == HttpMethod.Post)
        {
            var selector = request.Content!.ReadAsStringAsync().Result;
            foreach (var (role, boundId) in BoundByTheModel)
                if (selector.Contains(role.Id.ToString()))
                    return BoundSnapshot(role, boundId);
            return Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""");
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static readonly (Role Role, Guid BoundId)[] BoundByTheModel =
    [
        (PipelineRoles.PipelineRun, PipelineRunArchetypeId),
        (PipelineRoles.NodeRun, NodeRunArchetypeId),
        (PipelineRoles.RunSubject, RunSubjectPredicateId),
        (PipelineRoles.SentMessage, SentMessageArchetypeId),
        (PipelineRoles.ProducedThing, ProducedPredicateId),
        (PipelineRoles.RunResumption, ResumptionConnectionId),
    ];

    private static readonly Guid BindsPredicateId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static HttpResponseMessage BoundSnapshot(Role role, Guid boundId) =>
        Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            snapshot = new
            {
                things = new[]
                {
                    new { id = role.Id, name = $"{role.Word} role" },
                    new { id = BindsPredicateId, name = ModelNames.Binds },
                    new { id = boundId, name = "whatever this model calls it" },
                },
                relationships = new[]
                {
                    new { id = Guid.NewGuid(), subjectId = role.Id, predicateId = BindsPredicateId, targetId = boundId },
                },
            },
        }));

    // The stand-in above answers a write with 200 whatever its body. Two rules the real broker applies are
    // applied here as well, because a stand-in that accepts anything is what let the writer's shape drift
    // from the route's and left every run unrecorded: a property is written as a typed envelope,
    // and a property is set only on a Thing that already carries it.
    private sealed class BrokerApplyingItsWriteRules
    {
        private readonly Dictionary<Guid, HashSet<string>> _propertiesByThing = new();

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/things" && request.Method == HttpMethod.Post)
                return Create(request);

            if (path.EndsWith("/properties", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
                return SetProperty(request, path);

            return RespondLikeAModelThatBindsItsRoles(request);
        }

        private HttpResponseMessage Create(HttpRequestMessage request)
        {
            var body = ReadJson(request).Result;
            var carried = new HashSet<string>(StringComparer.Ordinal);

            if (body.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object
                        || !property.Value.TryGetProperty("typeInfo", out _))
                        return new HttpResponseMessage(HttpStatusCode.BadRequest);
                    carried.Add(property.Name);
                }
            }

            _propertiesByThing[body.GetProperty("id").GetGuid()] = carried;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private HttpResponseMessage SetProperty(HttpRequestMessage request, string path)
        {
            var thingId = Guid.Parse(path.Split('/')[3]);
            var name = ReadJson(request).Result.GetProperty("name").GetString()!;

            return _propertiesByThing.TryGetValue(thingId, out var carried) && carried.Contains(name)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

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
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeTrue();
        // The only names on the selector are the built-in predicates: an archetype name asked for here
        // is what stopped finding anything the moment a model renamed one.
        selector.GetProperty("names").EnumerateArray().Select(name => name.GetString())
            .Should().BeEquivalentTo(["is", "has", "binds"]);
        selector.TryGetProperty("types", out _).Should().BeFalse();
        selector.TryGetProperty("markedTypes", out _).Should().BeFalse();
        selector.TryGetProperty("markedArchetypes", out _).Should().BeFalse();

        selector.GetProperty("boundTypes").EnumerateArray().Select(role => role.GetGuid())
            .Should().BeEquivalentTo([PipelineRoles.Port.Id, PipelineRoles.PipelineWire.Id]);

        // The pipeline, and the Thing for every role, so one missing from the snapshot means the model binds it
        // to nothing rather than that this pipeline has no node playing it, beside the roles an end standing for
        // a system is read through.
        selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
            .Should().BeEquivalentTo(PipelineRoles.DagRoles.Select(role => role.Id).Append(pipelineId).Concat(
                [PipelineRoles.ExternalSystem.Id, PipelineRoles.StandsFor.Id, PipelineRoles.ToldThrough.Id, PipelineRoles.Choice.Id,
                 PipelineRoles.CompletionRange.Id, PipelineRoles.AwaitsThrough.Id]));

        // What each role binds, then the system an end stands for, the connection it is told through, what a port
        // offers and a node chose, and the state a waiting node waits for, are reached after the nodes.
        selector.GetProperty("traverse").EnumerateArray()
            .Select(rule => rule.TryGetProperty("predicateRole", out var role) ? role.GetString() : rule.GetProperty("predicate").GetString())
            .Should().Equal(ModelNames.Has, ModelNames.Binds, PipelineRoles.StandsFor.Id.ToString(),
                PipelineRoles.ToldThrough.Id.ToString(), PipelineRoles.Choice.Id.ToString(), PipelineRoles.CompletionRange.Id.ToString());
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
        await Settle.UntilAsync(
            () => RequestsTo(handler, $"/api/subscriptions/{subscriptionId}", HttpMethod.Delete).Count == 1,
            "the gateway releases the subscription it opened");
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
        created.GetProperty("properties").GetProperty("status").GetProperty("value").GetString()
            .Should().Be(RunStatus.Running);
        created.GetProperty("properties").GetProperty("pipelineId").GetProperty("value").GetString()
            .Should().Be(pipelineId.ToString());

        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().HaveCount(2);
    }

    // The broker reads each property as { typeInfo, value } and refuses a bare one with a 400 carrying no
    // body, so a run answered its caller and left nothing behind.
    [Fact]
    public async Task CreateRunAsync_WritesEveryPropertyAsATypedEnvelope()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var written = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single()))
            .GetProperty("properties").EnumerateObject().ToList();
        written.Should().NotBeEmpty();
        foreach (var property in written)
            property.Value.GetProperty("typeInfo").GetString().Should().Be("vos.String");
    }

    // The message is whole before it is related to the system: that last write is the one the broker
    // dispatches to the sender, which reads the message it is sent.
    [Fact]
    public async Task SendMessageAsync_WritesTheMessageHoldsItOnTheRunAndRelatesItToTheSystemAlongItsConnectionLast()
    {
        var runId = Guid.NewGuid();
        var telling = new Telling(Guid.NewGuid(), "Reporting office", Guid.NewGuid());
        var (gateway, handler) = NewGateway();

        await gateway.SendMessageAsync(runId, telling, JsonDocument.Parse("""{"result":"world"}""").RootElement, CancellationToken.None);

        var message = await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single());
        var messageId = message.GetProperty("id").GetString();
        JsonDocument.Parse(message.GetProperty("properties").GetProperty("payload").GetProperty("value").GetString()!)
            .RootElement.GetProperty("result").GetString().Should().Be("world");

        var edges = new List<JsonElement>();
        foreach (var request in RequestsTo(handler, "/api/relationships", HttpMethod.Post))
            edges.Add(await ReadJson(request));
        edges.Select(edge => (edge.GetProperty("subjectId").GetString(), edge.GetProperty("targetId").GetString())).Should().Equal(
            (messageId, SentMessageArchetypeId.ToString()),
            (runId.ToString(), messageId),
            (messageId, telling.SystemId.ToString()));
        edges[^1].GetProperty("predicateId").GetString().Should().Be(telling.ConnectionId.ToString());
    }

    // The result is known only once the run ends, and the property route sets a property the Thing already
    // carries — so a run that does not declare it at creation can never be given one.
    [Fact]
    public async Task ARunIsCreatedCarryingTheResultItLaterPublishes()
    {
        var runId = Guid.NewGuid();
        var (gateway, _) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        var publishing = () => gateway.SetRunResultAsync(
            runId, JsonDocument.Parse("""{"total":3}""").RootElement, CancellationToken.None);

        await publishing.Should().NotThrowAsync();
    }

    // Read in the wrong casing the predicate is not found at all, so the run is created and then left with
    // no relationships — reaching neither the archetype that makes it a run nor the pipeline it ran.
    [Fact]
    public async Task CreateRunAsync_WritesEachEdgeAgainstThePredicateIdentifierTheBrokerNamed()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        foreach (var request in RequestsTo(handler, "/api/relationships", HttpMethod.Post))
            (await ReadJson(request)).GetProperty("predicateId").GetString()
                .Should().Be(PredicateId.ToString());
    }

    // The subject is a Thing the model holds, so the run reaches it by a relationship along the predicate
    // the model marks for it — never by its identifier copied into a word.
    [Fact]
    public async Task CreateRunAsync_WithASubject_RelatesTheRunToItAlongTheMarkedPredicate()
    {
        var runId = Guid.NewGuid();
        var subject = new RunSubject(Guid.NewGuid(), "Submission 42");
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None, subject);

        var edges = new List<JsonElement>();
        foreach (var request in RequestsTo(handler, "/api/relationships", HttpMethod.Post))
            edges.Add(await ReadJson(request));
        edges.Should().HaveCount(3);
        var toSubject = edges.Single(edge => edge.GetProperty("predicateId").GetString() == RunSubjectPredicateId.ToString());
        toSubject.GetProperty("subjectId").GetString().Should().Be(runId.ToString());
        toSubject.GetProperty("targetId").GetString().Should().Be(subject.Id.ToString());
    }

    // What the identifier names is outside the model, so the run carries it as a plain property.
    [Fact]
    public async Task CreateRunAsync_WithTheRequestThatStartedIt_CarriesItsIdentifier_AndWithoutOneCarriesNone()
    {
        var requestId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, requestId: requestId);
        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var created = new List<JsonElement>();
        foreach (var request in RequestsTo(handler, "/api/things", HttpMethod.Post))
            created.Add((await ReadJson(request)).GetProperty("properties"));
        created[0].GetProperty("requestId").GetProperty("value").GetString().Should().Be(requestId.ToString());
        created[1].TryGetProperty("requestId", out _).Should().BeFalse();
    }

    // A model binding no such predicate still records the run; it only loses what it cannot say.
    [Fact]
    public async Task CreateRunAsync_WhenTheModelBindsNoSubjectPredicate_RecordsTheRunWithoutIt()
    {
        var broker = new BrokerApplyingItsWriteRules();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions" && request.Method == HttpMethod.Post
            && request.Content!.ReadAsStringAsync().Result.Contains(PipelineRoles.RunSubject.Id.ToString())
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : broker.Respond(request));

        var creating = () => gateway.CreateRunAsync(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, new RunSubject(Guid.NewGuid(), "orphan"));

        await creating.Should().NotThrowAsync();
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadStartSubgraphAsync_AsksForTheTargetWhatStandsForItAndWhatItStarts()
    {
        var targetId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadStartSubgraphAsync(targetId, CancellationToken.None);

        var selector = await ReadJson(RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Single());
        selector.GetProperty("ids")[0].GetString().Should().Be(targetId.ToString());
        selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeTrue();
        selector.TryGetProperty("types", out _).Should().BeFalse();
        selector.TryGetProperty("markedArchetypes", out _).Should().BeFalse();

        // The role Things are asked for by identifier, so what each binds can be read off the snapshot.
        selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
            .Should().Contain([PipelineRoles.StateWatch.Id, PipelineRoles.StandsFor.Id, PipelineRoles.PipelineStart.Id,
                PipelineRoles.Pipeline.Id, PipelineRoles.PipelineInput.Id, PipelineRoles.Connection.Id]);

        // Traversals expand in order, so the watched state has to be reached before the nodes standing for it.
        var traversals = selector.GetProperty("traverse").EnumerateArray()
            .Select(rule => (Role: rule.TryGetProperty("predicateRole", out var r) ? r.GetString() : null,
                             Name: rule.TryGetProperty("predicate", out var n) ? n.GetString() : null,
                             Direction: rule.GetProperty("direction").GetString()))
            .ToList();
        traversals.Should().ContainInOrder(
            (null, ModelNames.Binds, "outgoing"),
            (PipelineRoles.StateWatch.Id.ToString(), null, "outgoing"),
            (PipelineRoles.StandsFor.Id.ToString(), null, "incoming"),
            (null, ModelNames.Has, "incoming"));
        traversals.Should().Contain((PipelineRoles.PipelineStart.Id.ToString(), null, "outgoing"));

        // Relationship rules keep the read to those edges: a state connection accumulates one dispatch
        // record per entry, and the wider reading would carry every one on every start.
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        selector.GetProperty("relationships").EnumerateArray().Should().HaveCountGreaterThan(0);
    }

    // The name route answers one Thing, so an answer that is not one is an answer the gateway cannot use.
    // Resolving the first entry of a list would tie the run to whatever the model happened to return first.
    [Theory]
    [InlineData("""{"Name":"is"}""")]
    [InlineData("""[{"Id":"33333333-3333-3333-3333-333333333333","Name":"is"}]""")]
    public async Task CreateRunAsync_WhenTheAnswerNamesNoIdentifier_TheFailureNamesThePredicate(string answer)
    {
        var (gateway, _) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/things" && request.RequestUri.Query.Contains("name=")
                ? Json(HttpStatusCode.OK, answer)
                : RespondLikeAModelThatBindsItsRoles(request));

        var creating = () => gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await creating.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{ModelNames.Is}*");
    }

    [Fact]
    public async Task SetNodeRunStatusAsync_FirstTime_CreatesTheNodeRunAndAttachesItToTheRun()
    {
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Echo", RunStatus.Running, error: null, CancellationToken.None);

        var created = await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single());
        created.GetProperty("name").GetString().Should().Be("NodeRun Echo");
        created.GetProperty("properties").GetProperty("status").GetProperty("value").GetString()
            .Should().Be(RunStatus.Running);
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().HaveCount(2);
    }

    // A NodeRun is created before its first status is known to be its last, and an error arrives only on a
    // failure — so the Thing declares both, or the transition that fails has nowhere to write.
    [Fact]
    public async Task SetNodeRunStatusAsync_WritesEveryPropertyAsATypedEnvelope()
    {
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Echo", RunStatus.Running, error: null, CancellationToken.None,
            index: 0, total: 2);

        var written = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single()))
            .GetProperty("properties").EnumerateObject().ToList();
        written.Select(property => property.Name)
            .Should().BeEquivalentTo(["status", "nodeId", "error", "index", "total",
                ModelNames.StartedUtc, ModelNames.FinishedUtc, ModelNames.Outputs, ModelNames.Attempts]);
        foreach (var property in written)
            property.Value.GetProperty("typeInfo").GetString().Should().Be("vos.String");
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
        var nodeRunId = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single())).GetProperty("id").GetGuid();
        var propertyWrites = handler.Requests.Where(request =>
            request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/properties")).ToList();
        propertyWrites.Should().OnlyContain(request => request.RequestUri!.AbsolutePath.Contains(nodeRunId.ToString()));
        (await PropertiesWritten(handler, nodeRunId)).Should().Contain(("status", RunStatus.Succeeded));
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
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);

        var written = await PropertiesWritten(handler, runId);
        written.Should().Contain(("status", RunStatus.Succeeded));
        written.Select(w => w.Name).Should().Contain(ModelNames.FinishedUtc);
    }

    // A record read back by a trace or a resumed run needs what the run was asked and when it ended; both
    // are declared at creation, because the property route sets only what a Thing already carries.
    [Fact]
    public async Task CreateRunAsync_KeepsTheParameters_AndDeclaresTheFinish()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None,
            parameters: JsonSerializer.SerializeToElement(new { greeting = "hello" }));

        var properties = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single())).GetProperty("properties");
        properties.GetProperty(ModelNames.Parameters).GetProperty("value").GetString().Should().Be("""{"greeting":"hello"}""");
        properties.GetProperty(ModelNames.FinishedUtc).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        properties.GetProperty(ModelNames.StartedUtc).GetProperty("value").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Waiting_is_written_with_the_outputs_but_is_not_a_finish_for_a_node_run_or_a_run()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);
        var outputs = new Dictionary<string, JsonElement> { ["notice"] = JsonSerializer.SerializeToElement("sent") };

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Waiting, null, CancellationToken.None, outputs: outputs);
        await gateway.SetRunStatusAsync(runId, RunStatus.Waiting, CancellationToken.None);

        var nodeRunId = (await Task.WhenAll(RequestsTo(handler, "/api/things", HttpMethod.Post).Select(ReadJson)))
            .Select(body => body.GetProperty("id").GetGuid()).Single(id => id != runId);
        var nodeRunWritten = await PropertiesWritten(handler, nodeRunId);
        nodeRunWritten.Should().Contain((ModelNames.Outputs, """{"notice":"sent"}"""));
        nodeRunWritten.Select(w => w.Name).Should().NotContain(ModelNames.FinishedUtc);
        (await PropertiesWritten(handler, runId)).Select(w => w.Name).Should().NotContain(ModelNames.FinishedUtc);
    }

    [Fact]
    public async Task PlaceVigilAsync_AsksForAVigilNamingTheResumptionConnection_AndAnswersItsIdentifier()
    {
        var subjectId = Guid.NewGuid();
        var rangeId = Guid.NewGuid();
        var vigilId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/vigils"
            ? Json(HttpStatusCode.Created, $$"""{"Id":"{{vigilId}}","Name":"Plot 7 enters decided","DispatchState":null}""")
            : RespondLikeAModelThatBindsItsRoles(request));

        var placement = await gateway.PlaceVigilAsync(subjectId, rangeId, CancellationToken.None);

        placement.Should().Be(new VigilPlacement.Placed(vigilId));
        var asked = await ReadJson(RequestsTo(handler, "/api/vigils", HttpMethod.Post).Single());
        asked.GetProperty("subjectId").GetGuid().Should().Be(subjectId);
        asked.GetProperty("rangeId").GetGuid().Should().Be(rangeId);
        asked.GetProperty("connectionId").GetGuid().Should().Be(ResumptionConnectionId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PlaceVigilAsync_WhenThePlatformRefuses_AnswersItsReason(HttpStatusCode refusal)
    {
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/vigils"
            ? Json(refusal, """{"error":"Range 'decided' does not judge 'Plot 7'"}""")
            : RespondLikeAModelThatBindsItsRoles(request));

        var placement = await gateway.PlaceVigilAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        placement.Should().Be(new VigilPlacement.Refused("Range 'decided' does not judge 'Plot 7'"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task IsVigilLiveAsync_IsFalseOnlyWhenThePlatformNoLongerHoldsIt(HttpStatusCode answer, bool live)
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(answer));

        (await gateway.IsVigilLiveAsync(Guid.NewGuid(), CancellationToken.None)).Should().Be(live);
    }

    [Fact]
    public async Task SetNodeRunStatusAsync_Terminal_WritesTheOutputsAndTheFinish()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        var outputs = new Dictionary<string, JsonElement> { ["echo"] = JsonSerializer.SerializeToElement("hi") };

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Succeeded, null, CancellationToken.None, outputs: outputs);

        var created = await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single());
        var nodeRunId = created.GetProperty("id").GetGuid();
        var written = await PropertiesWritten(handler, nodeRunId);
        written.Should().Contain(("status", RunStatus.Succeeded));
        written.Should().Contain((ModelNames.Outputs, """{"echo":"hi"}"""));
        written.Select(w => w.Name).Should().Contain(ModelNames.FinishedUtc);
    }

    // A branch not taken, or a node whose first write is its last, is created already finished.
    [Fact]
    public async Task SetNodeRunStatusAsync_CreatedAlreadyTerminal_CarriesItsFinishAndOutputsFromTheStart()
    {
        var (gateway, handler) = NewGateway();
        var outputs = new Dictionary<string, JsonElement> { ["echo"] = JsonSerializer.SerializeToElement("hi") };

        await gateway.SetNodeRunStatusAsync(Guid.NewGuid(), Guid.NewGuid(), "Echo", RunStatus.NotTaken, null, CancellationToken.None, outputs: outputs);

        var properties = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single())).GetProperty("properties");
        properties.GetProperty(ModelNames.FinishedUtc).GetProperty("value").GetString().Should().NotBeNullOrEmpty();
        properties.GetProperty(ModelNames.Outputs).GetProperty("value").GetString().Should().Be("""{"echo":"hi"}""");
        handler.Requests.Where(request => request.Method == HttpMethod.Put).Should().BeEmpty();
    }

    [Fact]
    public async Task RelateProducedAsync_RelatesTheNodeRunToEachThingAlongTheMarkedPredicate()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Maker", RunStatus.Running, null, CancellationToken.None);
        var nodeRunId = (await ReadJson(RequestsTo(handler, "/api/things", HttpMethod.Post).Single())).GetProperty("id").GetGuid();

        await gateway.RelateProducedAsync(runId, nodeId, null, [one, two], CancellationToken.None);

        var related = new List<(Guid Subject, Guid Predicate, Guid Target)>();
        foreach (var request in RequestsTo(handler, "/api/relationships", HttpMethod.Post))
        {
            var body = await ReadJson(request);
            related.Add((body.GetProperty("subjectId").GetGuid(), body.GetProperty("predicateId").GetGuid(), body.GetProperty("targetId").GetGuid()));
        }
        related.Should().Contain((nodeRunId, ProducedPredicateId, one)).And.Contain((nodeRunId, ProducedPredicateId, two));
    }

    [Fact]
    public async Task RelateProducedAsync_WhenTheModelMarksNoPredicate_WritesNothing()
    {
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var relating = () => gateway.RelateProducedAsync(Guid.NewGuid(), Guid.NewGuid(), null, [Guid.NewGuid()], CancellationToken.None);

        await relating.Should().NotThrowAsync();
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().BeEmpty();
    }

    private static async Task<List<(string Name, string? Value)>> PropertiesWritten(MockHttpMessageHandler handler, Guid thingId)
    {
        var written = new List<(string Name, string? Value)>();
        foreach (var request in RequestsTo(handler, $"/api/things/{thingId}/properties", HttpMethod.Put))
        {
            var body = await ReadJson(request);
            written.Add((body.GetProperty("name").GetString()!, body.GetProperty("value").GetString()));
        }
        return written;
    }

    // A run started by a state entry is awaited by nobody, so the reason it failed has to be on the run
    // itself — and the property route sets only what a Thing already carries, so the run is created
    // carrying it, as it is created carrying its result.
    [Fact]
    public async Task SetRunStatusAsync_WithAReason_WritesTheReasonOnTheRunItWasCreatedCarrying()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        var failing = () => gateway.SetRunStatusAsync(runId, RunStatus.Failed, CancellationToken.None, "'subject' is not an output port");

        await failing.Should().NotThrowAsync();
        var written = new List<(string Name, string Value)>();
        foreach (var request in RequestsTo(handler, $"/api/things/{runId}/properties", HttpMethod.Put))
        {
            var body = await ReadJson(request);
            written.Add((body.GetProperty("name").GetString()!, body.GetProperty("value").GetString()!));
        }
        written.Should().Contain(("status", RunStatus.Failed));
        written.Should().Contain(("error", "'subject' is not an output port"));
    }

    [Fact]
    public async Task SetRunResultAsync_PublishesThePipelineResultOnTheRun()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);
        var result = JsonDocument.Parse("""{"total":3}""").RootElement;

        await gateway.SetRunResultAsync(runId, result, CancellationToken.None);

        var body = await ReadJson(RequestsTo(handler, $"/api/things/{runId}/properties", HttpMethod.Put).Single());
        body.GetProperty("name").GetString().Should().Be("result");
        body.GetProperty("value").GetString().Should().Contain("\"total\":3");
    }

    // The property set is named the way the broker names it; only the flag inside keeps the casing the
    // model gave it. Read as camelCase the set is never found, so a cancelled run runs on to the end.
    [Theory]
    [InlineData("""{"Properties":{"cancelRequested":{"value":true,"typeInfo":"vos.Boolean"}}}""", true)]
    [InlineData("""{"Properties":{"cancelRequested":{"value":"true","typeInfo":"vos.String"}}}""", true)]
    [InlineData("""{"Properties":{"cancelRequested":{"value":"TRUE","typeInfo":"vos.String"}}}""", true)]
    [InlineData("""{"Properties":{"cancelRequested":true}}""", true)]
    [InlineData("""{"Properties":{"cancelRequested":{"value":false,"typeInfo":"vos.Boolean"}}}""", false)]
    [InlineData("""{"Properties":{"cancelRequested":{"value":"no","typeInfo":"vos.String"}}}""", false)]
    [InlineData("""{"Properties":{}}""", false)]
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
    public async Task PredicateOfAsync_ReadsTheRelationshipAndAnswersItsPredicate()
    {
        var relationshipId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(_ => Json(HttpStatusCode.OK,
            $$$"""{"Id":"{{{relationshipId}}}","SubjectId":"{{{Guid.NewGuid()}}}","PredicateId":"{{{PredicateId}}}","TargetId":"{{{Guid.NewGuid()}}}","Properties":{}}"""));

        (await gateway.PredicateOfAsync(relationshipId, CancellationToken.None)).Should().Be(PredicateId);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/api/relationships/{relationshipId}");
    }

    [Fact]
    public async Task PredicateOfAsync_WhenTheModelDoesNotHoldTheRelationship_AnswersNone()
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await gateway.PredicateOfAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
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
            if ((await request.Content!.ReadAsStringAsync()).Contains(PipelineRoles.NodeRun.Id.ToString()))
                nodeRunLookups++;
        nodeRunLookups.Should().Be(1);
    }

    // The run archetype is asked for on its own, with only the binding that reaches it. Through boundTypes, or
    // with every relationship touching it, the answer would have carried every run the model has ever recorded.
    [Fact]
    public async Task ResolvingARoleArchetype_AsksForTheArchetypeWithoutItsMembers()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var lookup = await ReadJson(RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Single());
        lookup.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
            .Should().BeEquivalentTo([PipelineRoles.PipelineRun.Id]);
        lookup.TryGetProperty("boundTypes", out _).Should().BeFalse();
        lookup.GetProperty("includeIsAncestors").GetBoolean().Should().BeFalse();
        lookup.GetProperty("relationships").EnumerateArray()
            .Select(rule => rule.GetProperty("predicate").GetString()).Should().Equal(ModelNames.Binds);
    }

    [Fact]
    public async Task WhenTheModelBindsNoRunArchetype_TheFailureNamesTheRole()
    {
        var (gateway, _) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/subscriptions"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var creating = () => gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await creating.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*role {PipelineRoles.PipelineRun.Word}*");
    }

    private static async Task<JsonElement> ReadJson(HttpRequestMessage request) =>
        JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync());
}
