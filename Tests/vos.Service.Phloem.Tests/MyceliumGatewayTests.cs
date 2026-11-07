using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Tests.Shared;
using Xunit;
using vos.Service.Shared.Roles;

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
    private static readonly Guid AttemptArchetypeId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ResumptionConnectionId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid StoppedByPredicateId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid StopRequestArchetypeId = Guid.Parse("13131313-1313-1313-1313-131313131313");
    private static readonly Guid RequestedByPredicateId = Guid.Parse("14141414-1414-1414-1414-141414141414");
    private static readonly Guid RunStoppingPredicateId = Guid.Parse("15151515-1515-1515-1515-151515151515");
    private static readonly Guid HeldVigilPredicateId = Guid.Parse("16161616-1616-1616-1616-161616161616");
    private static readonly Guid OrchestratorId = Guid.Parse("17171717-1717-1717-1717-171717171717");
    private static readonly Guid HasPredicateId = Guid.Parse("18181818-1818-1818-1818-181818181818");

    private static (MyceliumGateway Gateway, MockHttpMessageHandler Handler) NewGateway(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null, TimeSpan? cadenceStreamRetry = null, TimeSpan? disposeWait = null)
    {
        var handler = new MockHttpMessageHandler(respond ?? new BrokerApplyingItsWriteRules().Respond);
        var gateway = new MyceliumGateway(
            new PerCallHttpClientFactory(handler),
            NullLogger<MyceliumGateway>.Instance,
            MyceliumUrl,
            "svc-token")
        {
            CadenceStreamRetry = cadenceStreamRetry ?? TimeSpan.FromSeconds(2),
            DisposeWait = disposeWait ?? TimeSpan.FromSeconds(5),
        };
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

        if (uri.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post)
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
        (PipelineRoles.Attempt, AttemptArchetypeId),
        (PipelineRoles.StoppedBy, StoppedByPredicateId),
        (PipelineRoles.StopRequest, StopRequestArchetypeId),
        (PipelineRoles.RequestedBy, RequestedByPredicateId),
        (PipelineRoles.RunStopping, RunStoppingPredicateId),
        (PipelineRoles.HeldVigil, HeldVigilPredicateId),
        (PipelineRoles.Orchestrator, OrchestratorId),
    ];

    // The relationship to the Thing is the write the broker sends back as a stop, so it comes after the one naming the run.
    [Fact]
    public async Task WriteStopRequestAsync_WritesTheRequestWithItsReason_NamesTheRun_AndRelatesItToTheThingLast()
    {
        var run = Guid.NewGuid();
        var thing = Guid.NewGuid();
        var (gateway, handler) = NewGateway(RespondLikeAModelThatBindsItsRoles);

        await gateway.WriteStopRequestAsync(run, thing, "the site is retired", CancellationToken.None);

        var fragment = await ReadJson(RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Single());
        var created = fragment.GetProperty("things").EnumerateArray().Single();
        var request = created.GetProperty("id").GetGuid();
        created.GetProperty("properties").GetProperty(ModelNames.Reason).GetProperty("value").GetString().Should().Be("the site is retired");
        fragment.GetProperty("relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("subject").GetGuid(), edge.GetProperty("predicate").GetGuid(), edge.GetProperty("target").GetGuid()))
            .Should().Equal((request, PredicateId, StopRequestArchetypeId), (request, RequestedByPredicateId, run));
        var stops = await ReadJson(RequestsTo(handler, "/api/relationships", HttpMethod.Post).Single());
        (stops.GetProperty("subjectId").GetGuid(), stops.GetProperty("predicateId").GetGuid(), stops.GetProperty("targetId").GetGuid())
            .Should().Be((request, RunStoppingPredicateId, thing));
        handler.Requests.Last().RequestUri!.AbsolutePath.Should().Be("/api/relationships");
    }

    [Fact]
    public async Task RelateHeldVigilAsync_WhenTheBrokerCannotFindAnEnd_FailsWithTheBrokersReason()
    {
        var vigil = Guid.NewGuid();
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/relationships" && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.NotFound, $$"""{"error":"Target not found: {{vigil}}"}""")
            : RespondLikeAModelThatBindsItsRoles(request));

        var relating = () => gateway.RelateHeldVigilAsync(Guid.NewGuid(), Guid.NewGuid(), vigil, CancellationToken.None);

        var refused = await relating.Should().ThrowAsync<HttpRequestException>();
        refused.WithMessage($"*Target not found: {vigil}*");
        refused.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RelateStoppedByAsync_RelatesTheRunToTheRequestAlongThePredicateTheRoleBinds()
    {
        var run = Guid.NewGuid();
        var request = Guid.NewGuid();
        var (gateway, handler) = NewGateway(RespondLikeAModelThatBindsItsRoles);

        await gateway.RelateStoppedByAsync(run, request, CancellationToken.None);

        var related = await ReadJson(RequestsTo(handler, "/api/relationships", HttpMethod.Post).Single());
        related.GetProperty("subjectId").GetGuid().Should().Be(run);
        related.GetProperty("predicateId").GetGuid().Should().Be(StoppedByPredicateId);
        related.GetProperty("targetId").GetGuid().Should().Be(request);
    }

    [Fact]
    public async Task RecordAttemptAsync_WritesTheAttemptAsAThingTheNodeRunHolds_AndMovesTheNodeRunsCountToItsNumber()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var noon = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);

        await gateway.RecordAttemptAsync(runId, nodeId, null, new AttemptRecord(2, noon, noon.AddSeconds(1), RunStatus.Failed, "the service was away"), CancellationToken.None);

        var created = await CreatedThings(handler);
        var nodeRunId = created[1].GetProperty("id").GetGuid();
        var attempt = created[2];
        var attemptId = attempt.GetProperty("id").GetGuid();
        var properties = attempt.GetProperty("properties");
        properties.GetProperty(ModelNames.Number).GetProperty("value").GetString().Should().Be("2");
        properties.GetProperty(ModelNames.StartedUtc).GetProperty("value").GetString().Should().Be(noon.ToString("o"));
        properties.GetProperty(ModelNames.FinishedUtc).GetProperty("value").GetString().Should().Be(noon.AddSeconds(1).ToString("o"));
        properties.GetProperty(ModelNames.Status).GetProperty("value").GetString().Should().Be(RunStatus.Failed);
        properties.GetProperty(ModelNames.Error).GetProperty("value").GetString().Should().Be("the service was away");
        var edges = await CreatedRelationships(handler);
        edges.Should().Contain(edge => edge.Subject == attemptId && edge.Target == AttemptArchetypeId);
        edges.Should().Contain(edge => edge.Subject == nodeRunId && edge.Target == attemptId);
        (await PropertiesWrittenSinceCreation(handler, nodeRunId)).Should().Contain((ModelNames.Attempts, "2"));
    }

    [Fact]
    public async Task RecordAttemptAsync_WritesTheAttemptItsKindAndTheNodeRunHoldingItInOneFragment()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var noon = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var (gateway, handler) = NewGateway();
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        var writesBefore = handler.Requests.Count;

        await gateway.RecordAttemptAsync(runId, nodeId, null, new AttemptRecord(1, noon, noon.AddSeconds(1), RunStatus.Failed, null), CancellationToken.None);

        var writes = handler.Requests.Skip(writesBefore).Where(request => request.Method != HttpMethod.Get).ToList();
        writes.Where(request => request.RequestUri!.AbsolutePath is "/api/things" or "/api/relationships").Should().BeEmpty();
        var fragment = await ReadJson(writes.Single(request => request.RequestUri!.AbsolutePath == "/api/model/fragment"));
        var attemptId = fragment.GetProperty("things").EnumerateArray().Single().GetProperty("id").GetGuid();
        var nodeRunId = (await CreatedThings(handler))[0].GetProperty("id").GetGuid();
        fragment.GetProperty("relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("subject").GetGuid(), edge.GetProperty("predicate").GetGuid(), edge.GetProperty("target").GetGuid()))
            .Should().Equal((attemptId, PredicateId, AttemptArchetypeId), (nodeRunId, PredicateId, attemptId));
    }

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

            if (path == "/api/model/fragment" && request.Method == HttpMethod.Post)
                return ReadJson(request).Result.GetProperty("things").EnumerateArray()
                    .Select(Create)
                    .FirstOrDefault(answer => !answer.IsSuccessStatusCode) ?? new HttpResponseMessage(HttpStatusCode.OK);

            if (path.EndsWith("/properties", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
                return SetProperty(request, path);

            return RespondLikeAModelThatBindsItsRoles(request);
        }

        private HttpResponseMessage Create(JsonElement body)
        {
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

            var id = body.GetProperty("id").GetGuid();
            if (_propertiesByThing.TryGetValue(id, out var already)) already.UnionWith(carried);
            else _propertiesByThing[id] = carried;
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

    // A later fragment naming the same Thing changes it, so only a Thing's first fragment creates it.
    private static async Task<IReadOnlyList<JsonElement>> CreatedThings(MockHttpMessageHandler handler)
    {
        var created = new List<JsonElement>();
        var seen = new HashSet<Guid>();
        foreach (var request in RequestsTo(handler, "/api/model/fragment", HttpMethod.Post))
        {
            var named = (await ReadJson(request)).GetProperty("things").EnumerateArray();
            created.AddRange(named.Where(thing => seen.Add(thing.GetProperty("id").GetGuid())));
        }
        return created;
    }

    private static async Task<IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)>> CreatedRelationships(MockHttpMessageHandler handler)
    {
        var created = new List<(Guid, Guid, Guid)>();
        foreach (var request in handler.Requests.Where(request => request.Method == HttpMethod.Post))
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/relationships")
            {
                var edge = await ReadJson(request);
                created.Add((edge.GetProperty("subjectId").GetGuid(), edge.GetProperty("predicateId").GetGuid(), edge.GetProperty("targetId").GetGuid()));
            }
            if (path == "/api/model/fragment")
                created.AddRange((await ReadJson(request)).GetProperty("relationships").EnumerateArray().Select(edge =>
                    (edge.GetProperty("subject").GetGuid(), edge.GetProperty("predicate").GetGuid(), edge.GetProperty("target").GetGuid())));
        }
        return created;
    }

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
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadPipelineSubgraphAsync(pipelineId, CancellationToken.None);

        var selector = await ReadJson(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Single());
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeTrue();
        var onlyRule = selector.GetProperty("relationships").EnumerateArray().Should().ContainSingle().Which;
        onlyRule.GetProperty("direction").GetString().Should().Be("outgoing");
        onlyRule.TryGetProperty("predicate", out _).Should().BeFalse("a rule naming no predicate takes every predicate");
        onlyRule.TryGetProperty("predicateRole", out _).Should().BeFalse();
        // The only names on the selector are the built-in predicates: an archetype name asked for here
        // is what stopped finding anything the moment a model renamed one.
        selector.GetProperty("names").EnumerateArray().Select(name => name.GetString())
            .Should().BeEquivalentTo(["is", "has", "binds"]);
        selector.TryGetProperty("types", out _).Should().BeFalse();
        selector.TryGetProperty("markedTypes", out _).Should().BeFalse();
        selector.TryGetProperty("markedArchetypes", out _).Should().BeFalse();

        selector.TryGetProperty("boundTypes", out _).Should().BeFalse("every port and wire in the model is not this pipeline's");

        // The pipeline, and the Thing for every role, so one missing from the snapshot means the model binds it
        // to nothing rather than that this pipeline has no node playing it, beside the roles a tell or follow-on
        // node is read through.
        selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
            .Should().BeEquivalentTo(PipelineRoles.DagRoles.Select(role => role.Id).Append(pipelineId).Concat(
                [PipelineRoles.ExternalSystem.Id, PipelineRoles.StandsFor.Id, PipelineRoles.ToldThrough.Id, PipelineRoles.Choice.Id,
                 PipelineRoles.CompletionRange.Id, PipelineRoles.AwaitsThrough.Id, PipelineRoles.PipelineTell.Id, PipelineRoles.PipelineFollowOn.Id,
                 PipelineRoles.PipelineStop.Id, PipelineRoles.RequestedBy.Id]));

        var rules = selector.GetProperty("traverse").EnumerateArray().ToList();
        rules.Select(rule => rule.TryGetProperty("predicateRole", out var role) ? role.GetString() : rule.GetProperty("predicate").GetString())
            .Should().Equal(ModelNames.Has, ModelNames.Is, ModelNames.Has, ModelNames.Binds,
                PipelineRoles.InputConnector.Id.ToString(), PipelineRoles.OutputConnector.Id.ToString(), PipelineRoles.StandsFor.Id.ToString(),
                PipelineRoles.ToldThrough.Id.ToString(), PipelineRoles.Choice.Id.ToString(), PipelineRoles.CompletionRange.Id.ToString(),
                PipelineRoles.ComparesWith.Id.ToString());
        rules.Take(3).Select(rule => rule.GetProperty("depth").GetInt32()).Should().Equal(8, 1, 1);
        rules.Should().OnlyContain(rule => rule.GetProperty("direction").GetString() == "outgoing");
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
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, snapshot)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var graph = await gateway.LoadPipelineSubgraphAsync(pipelineId, CancellationToken.None);

        graph.Things.Should().HaveCount(3);
        graph.Thing(pipelineId)!.Name.Should().Be("Demo");
    }

    // Every subscription the broker holds is checked on every write, so a read that opens one costs every writer.
    [Fact]
    public async Task LoadPipelineSubgraphAsync_ReadsWithOneCallAndOpensNoSubscription()
    {
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    watermark = 7,
                    snapshot = new { things = Array.Empty<object>(), relationships = Array.Empty<object>() },
                }))
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await gateway.LoadPipelineSubgraphAsync(Guid.NewGuid(), CancellationToken.None);

        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}")
            .Should().Equal("POST /api/snapshots");
    }

    [Fact]
    public async Task LookUpVigilAsync_AVigilNoNodeRunHoldsYet_IsAVigilWithNoHolder()
    {
        var vigilArchetype = Guid.NewGuid();
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.OK, ThingOfType(vigilArchetype, vigilArchetype)));

        var lookup = await gateway.LookUpVigilAsync(Target, CancellationToken.None);

        lookup.Should().Be(new VigilLookup(IsVigil: true, Holder: null));
    }

    [Fact]
    public async Task LookUpVigilAsync_AThingOfAnotherType_IsNotAVigil()
    {
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.OK, ThingOfType(Guid.NewGuid(), Guid.NewGuid())));

        var lookup = await gateway.LookUpVigilAsync(Target, CancellationToken.None);

        lookup.Should().Be(new VigilLookup(IsVigil: false, Holder: null));
    }

    private static readonly Guid Target = Guid.NewGuid();

    private static string ThingOfType(Guid type, Guid boundByTheVigilRole)
    {
        var isPredicate = Guid.NewGuid();
        var binds = Guid.NewGuid();
        return JsonSerializer.Serialize(new
        {
            snapshot = new
            {
                things = new object[]
                {
                    new { id = Target, name = "Plot 7 enters decided", properties = new Dictionary<string, object>() },
                    new { id = type, name = "A type", properties = new Dictionary<string, object>() },
                    new { id = boundByTheVigilRole, name = "Vigil", properties = new Dictionary<string, object>() },
                    new { id = PipelineRoles.Vigil.Id, name = "vigil role", properties = new Dictionary<string, object>() },
                    new { id = isPredicate, name = "is", properties = new Dictionary<string, object>() },
                    new { id = binds, name = "binds", properties = new Dictionary<string, object>() },
                },
                relationships = new[]
                {
                    new { id = Guid.NewGuid(), subjectId = PipelineRoles.Vigil.Id, predicateId = binds, targetId = boundByTheVigilRole },
                    new { id = Guid.NewGuid(), subjectId = Target, predicateId = isPredicate, targetId = type },
                },
            },
        });
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

        var created = (await CreatedThings(handler)).Single();
        created.GetProperty("id").GetString().Should().Be(runId.ToString());
        created.GetProperty("properties").GetProperty("status").GetProperty("value").GetString()
            .Should().Be(RunStatus.Running);
        created.GetProperty("properties").GetProperty("pipelineId").GetProperty("value").GetString()
            .Should().Be(pipelineId.ToString());

        RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Should().ContainSingle("the run and its relationships are taken or refused together");
        RequestsTo(handler, "/api/things", HttpMethod.Post).Should().BeEmpty();
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().BeEmpty();
        (await CreatedRelationships(handler)).Should().BeEquivalentTo(new[]
        {
            (runId, PredicateId, PipelineRunArchetypeId),
            (runId, PredicateId, pipelineId),
            (OrchestratorId, PredicateId, runId),
        });
    }

    [Fact]
    public async Task CreateRunAsync_HoldsTheRunUnderTheOrchestratorInTheSameFragment()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        var fragment = await ReadJson(RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Single());
        fragment.GetProperty("relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("subject").GetGuid(), edge.GetProperty("target").GetGuid()))
            .Should().Contain((OrchestratorId, runId));
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRunAsync_WhenTheModelBindsNoOrchestrator_RecordsTheRunUnheld()
    {
        var broker = new BrokerApplyingItsWriteRules();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
            && request.Content!.ReadAsStringAsync().Result.Contains(PipelineRoles.Orchestrator.Id.ToString())
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : broker.Respond(request));

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await CreatedRelationships(handler)).Should().HaveCount(2);
    }

    [Fact]
    public async Task SetRunStatusAsync_Finished_ReleasesTheRunFromTheOrchestrator()
    {
        var runId = Guid.NewGuid();
        var otherRunId = Guid.NewGuid();
        var holding = Guid.NewGuid();
        var holdingAnother = Guid.NewGuid();
        var broker = new BrokerApplyingItsWriteRules();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
            && request.Content!.ReadAsStringAsync().Result.Contains(OrchestratorId.ToString())
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    snapshot = new
                    {
                        things = new object[]
                        {
                            new { id = OrchestratorId, name = "Phloem prototype" },
                            new { id = HasPredicateId, name = ModelNames.Has },
                        },
                        relationships = new object[]
                        {
                            new { id = holding, subjectId = OrchestratorId, predicateId = HasPredicateId, targetId = runId },
                            new { id = holdingAnother, subjectId = OrchestratorId, predicateId = HasPredicateId, targetId = otherRunId },
                        },
                    },
                }))
                : broker.Respond(request));
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);

        handler.Requests.Where(request => request.Method == HttpMethod.Delete).Select(request => request.RequestUri!.AbsolutePath)
            .Should().Equal($"/api/relationships/{holding}");
        var change = await ReadJson(RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Last());
        change.GetProperty("relationships").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task SetRunStatusAsync_Running_HoldsTheRunAgainInTheStatusFragment()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);
        var writtenBefore = handler.Requests.Count;

        await gateway.SetRunStatusAsync(runId, RunStatus.Running, CancellationToken.None, error: string.Empty);

        var change = handler.Requests.Skip(writtenBefore).Should().ContainSingle().Subject;
        change.RequestUri!.AbsolutePath.Should().Be("/api/model/fragment");
        (await ReadJson(change)).GetProperty("relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("subject").GetGuid(), edge.GetProperty("predicate").GetGuid(), edge.GetProperty("target").GetGuid()))
            .Should().Equal((OrchestratorId, PredicateId, runId));
    }

    [Fact]
    public async Task WaitingAndUnfinishedNodeRunsAreReadFromWhatTheOrchestratorHolds()
    {
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.WaitingNodeRunsAsync(CancellationToken.None);
        await gateway.UnfinishedNodeRunsAsync(CancellationToken.None);

        var selectors = await Task.WhenAll(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Select(ReadJson));
        selectors.Should().HaveCount(2);
        foreach (var selector in selectors)
        {
            selector.TryGetProperty("boundTypes", out _).Should().BeFalse("every node run ever recorded is not what a restart needs");
            selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
                .Should().BeEquivalentTo([PipelineRoles.Orchestrator.Id, PipelineRoles.AwaitedThing.Id, PipelineRoles.HeldVigil.Id]);
            selector.GetProperty("traverse").EnumerateArray()
                .Select(rule => (rule.GetProperty("predicate").GetString(), rule.GetProperty("direction").GetString(), rule.GetProperty("depth").GetInt32()))
                .Should().Equal((ModelNames.Binds, "outgoing", 1), (ModelNames.Has, "outgoing", 2));
            selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task CreateRunAsync_WhenTheBrokerRefusesTheRun_FailsWithTheBrokersReason()
    {
        var broker = new BrokerApplyingItsWriteRules();
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/model/fragment"
            ? Json(HttpStatusCode.BadRequest, """{"error":"the run kind is not a type"}""")
            : broker.Respond(request));

        var creating = () => gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await creating.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*the run kind is not a type*");
    }

    [Fact]
    public async Task CreateRunAsync_RecordsWhetherTheRunIsARehearsal()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, rehearsal: true);
        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var written = (await CreatedThings(handler))
            .Select(run => run.GetProperty("properties").GetProperty(ModelNames.Rehearsal).GetProperty("value").GetString());
        written.Should().Equal("true", "false");
    }

    [Fact]
    public async Task CreateRunAsync_WithARefusal_RecordsTheRunRefusedAndFinishedWithItsReasons()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, refusal: "Pipeline is invalid: 'Echo' needs its port 'message' filled.");

        var properties = (await CreatedThings(handler)).Single().GetProperty("properties");
        properties.GetProperty(ModelNames.Status).GetProperty("value").GetString().Should().Be(RunStatus.Refused);
        properties.GetProperty(ModelNames.Error).GetProperty("value").GetString().Should().Contain("'Echo' needs its port 'message'");
        properties.GetProperty(ModelNames.FinishedUtc).GetProperty("value").GetString().Should().NotBeNullOrEmpty();
    }

    // The broker reads each property as { typeInfo, value } and refuses a bare one with a 400 carrying no
    // body, so a run answered its caller and left nothing behind.
    [Fact]
    public async Task CreateRunAsync_WritesEveryPropertyAsATypedEnvelope()
    {
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var written = (await CreatedThings(handler)).Single()
            .GetProperty("properties").EnumerateObject().ToList();
        written.Should().NotBeEmpty();
        foreach (var property in written)
            property.Value.GetProperty("typeInfo").GetString().Should().Be("vos.String");
    }

    // The message is whole before it is related to the system: that last write is the one the broker
    // sends to the sender, which reads the message it is sent.
    [Fact]
    public async Task SendMessageAsync_WritesTheMessageHoldsItOnTheRunAndRelatesItToTheSystemAlongItsConnectionLast()
    {
        var runId = Guid.NewGuid();
        var telling = new Telling(Guid.NewGuid(), "Reporting office", Guid.NewGuid());
        var (gateway, handler) = NewGateway();

        await gateway.SendMessageAsync(runId, telling, JsonDocument.Parse("""{"result":"world"}""").RootElement, CancellationToken.None);

        RequestsTo(handler, "/api/things", HttpMethod.Post).Should().BeEmpty();
        var fragment = await ReadJson(RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Single());
        var message = fragment.GetProperty("things").EnumerateArray().Single();
        var messageId = message.GetProperty("id").GetGuid();
        JsonDocument.Parse(message.GetProperty("properties").GetProperty("payload").GetProperty("value").GetString()!)
            .RootElement.GetProperty("result").GetString().Should().Be("world");
        fragment.GetProperty("relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("subject").GetGuid(), edge.GetProperty("predicate").GetGuid(), edge.GetProperty("target").GetGuid()))
            .Should().Equal((messageId, PredicateId, SentMessageArchetypeId), (runId, PredicateId, messageId));

        var toTheSystem = await ReadJson(RequestsTo(handler, "/api/relationships", HttpMethod.Post).Single());
        (toTheSystem.GetProperty("subjectId").GetGuid(), toTheSystem.GetProperty("predicateId").GetGuid(), toTheSystem.GetProperty("targetId").GetGuid())
            .Should().Be((messageId, telling.ConnectionId, telling.SystemId));
        handler.Requests.Last().RequestUri!.AbsolutePath.Should().Be("/api/relationships");
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

        (await CreatedRelationships(handler)).Should().OnlyContain(edge => edge.Predicate == PredicateId);
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

        var edges = await CreatedRelationships(handler);
        edges.Should().HaveCount(4);
        edges.Single(edge => edge.Predicate == RunSubjectPredicateId).Should().Be((runId, RunSubjectPredicateId, subject.Id));
    }

    // What the identifier names is outside the model, so the run carries it as a plain property.
    [Fact]
    public async Task CreateRunAsync_WithTheRequestThatStartedIt_CarriesItsIdentifier_AndWithoutOneCarriesNone()
    {
        var requestId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, requestId: requestId);
        await gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var created = (await CreatedThings(handler)).Select(run => run.GetProperty("properties")).ToList();
        created[0].GetProperty("requestId").GetProperty("value").GetString().Should().Be(requestId.ToString());
        created[1].TryGetProperty("requestId", out _).Should().BeFalse();
    }

    // A model binding no such predicate still records the run; it only loses what it cannot say.
    [Fact]
    public async Task CreateRunAsync_WhenTheModelBindsNoSubjectPredicate_RecordsTheRunWithoutIt()
    {
        var broker = new BrokerApplyingItsWriteRules();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
            && request.Content!.ReadAsStringAsync().Result.Contains(PipelineRoles.RunSubject.Id.ToString())
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : broker.Respond(request));

        var creating = () => gateway.CreateRunAsync(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None, new RunSubject(Guid.NewGuid(), "orphan"));

        await creating.Should().NotThrowAsync();
        (await CreatedRelationships(handler)).Should().HaveCount(3);
    }

    [Fact]
    public async Task LoadStartSubgraphAsync_AsksForTheTargetWhatStandsForItAndWhatItStarts()
    {
        var targetId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadStartSubgraphAsync(targetId, [], CancellationToken.None);

        var selector = await ReadJson(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Single());
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

        // Relationship rules keep the read to those edges: a state connection accumulates one service call
        // record per entry, and the wider reading would carry every one on every start.
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        selector.GetProperty("relationships").EnumerateArray().Should().HaveCountGreaterThan(0);
        Rules(selector).Should().Contain((PipelineRoles.StandsFor.Id.ToString(), "outgoing"))
            .And.NotContain((null, "outgoing"), "every relationship out of a Thing in the read would carry each one it ever gained");
    }

    private static List<(string? Role, string Direction)> Rules(JsonElement selector) =>
        selector.GetProperty("relationships").EnumerateArray()
            .Where(rule => !rule.TryGetProperty("predicate", out _))
            .Select(rule => (rule.TryGetProperty("predicateRole", out var role) ? role.GetString() : null, rule.GetProperty("direction").GetString()!))
            .ToList();

    [Fact]
    public async Task LoadStartSubgraphAsync_AsksForEachThingTheArrivingThingRelatesToAndTheRolesThatSayWhatItIs()
    {
        var targetId = Guid.NewGuid();
        var kindId = Guid.NewGuid();
        var systemId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.LoadStartSubgraphAsync(targetId, [kindId, systemId], CancellationToken.None);

        var selector = await ReadJson(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Single());
        selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())
            .Should().Contain([targetId, kindId, systemId, PipelineRoles.MessageKind.Id, PipelineRoles.ExternalSystem.Id]);
        selector.TryGetProperty("boundTypes", out _).Should().BeFalse("every message kind and system in the model is not what this start offered");
    }

    [Fact]
    public async Task RelatedFromAsync_ReadsOnlyTheRelationshipsOutOfTheThing_AndAnswersWhereEachGoes()
    {
        var arrivingId = Guid.NewGuid();
        var kindId = Guid.NewGuid();
        var systemId = Guid.NewGuid();
        var snapshot = $$"""
        {
          "snapshot": {
            "things": [ { "id": "{{arrivingId}}", "name": "an arriving document", "properties": {} } ],
            "relationships": [
              { "id": "{{Guid.NewGuid()}}", "subjectId": "{{arrivingId}}", "predicateId": "{{Guid.NewGuid()}}", "targetId": "{{kindId}}", "properties": {} },
              { "id": "{{Guid.NewGuid()}}", "subjectId": "{{arrivingId}}", "predicateId": "{{Guid.NewGuid()}}", "targetId": "{{systemId}}", "properties": {} }
            ]
          }
        }
        """;
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, snapshot)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var related = await gateway.RelatedFromAsync(arrivingId, CancellationToken.None);

        related.Should().BeEquivalentTo([kindId, systemId]);
        var selector = await ReadJson(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Single());
        selector.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid()).Should().Equal(arrivingId);
        selector.GetProperty("includeIsAncestors").GetBoolean().Should().BeFalse();
        selector.GetProperty("includeRelationships").GetBoolean().Should().BeTrue();
        Rules(selector).Should().Equal([(null, "outgoing")], "relationships into the Thing are what piles up on it after it arrives");
    }

    [Fact]
    public async Task RelatedFromAsync_AThingTheModelDoesNotHold_RelatesToNothing()
    {
        var (gateway, _) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        (await gateway.RelatedFromAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();
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

        var created = (await CreatedThings(handler)).Single();
        created.GetProperty("name").GetString().Should().Be("NodeRun Echo");
        created.GetProperty("properties").GetProperty("status").GetProperty("value").GetString()
            .Should().Be(RunStatus.Running);
        RequestsTo(handler, "/api/model/fragment", HttpMethod.Post).Should().ContainSingle("the node run and its relationships are taken or refused together");
        (await CreatedRelationships(handler)).Should().HaveCount(2);
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

        var written = (await CreatedThings(handler)).Single()
            .GetProperty("properties").EnumerateObject().ToList();
        written.Select(property => property.Name)
            .Should().BeEquivalentTo(["status", "nodeId", "error", "index", "total",
                ModelNames.StartedUtc, ModelNames.FinishedUtc, ModelNames.Outputs, ModelNames.Attempts, ModelNames.Decision]);
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

        var nodeRunId = (await CreatedThings(handler)).Select(thing => thing.GetProperty("id").GetGuid()).Distinct()
            .Should().ContainSingle("the change names the node run it created").Subject;
        (await PropertiesWrittenSinceCreation(handler, nodeRunId)).Should().Contain(("status", RunStatus.Succeeded));
    }

    [Fact]
    public async Task SetNodeRunStatusAsync_WhenItFails_RecordsTheErrorAlongsideTheStatus()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Failed, "boom", CancellationToken.None);

        var nodeRunId = (await CreatedThings(handler)).First().GetProperty("id").GetGuid();
        (await PropertiesWrittenSinceCreation(handler, nodeRunId)).Should()
            .Contain((ModelNames.Status, RunStatus.Failed)).And.Contain((ModelNames.Error, "boom"));
    }

    // The broker commits each request on its own, and a caller holds its connection open while a run writes its state changes.
    [Fact]
    public async Task SetNodeRunStatusAsync_SecondTime_WritesTheWholeChangeInOneRequest()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Assess", RunStatus.Running, null, CancellationToken.None);
        var writtenBefore = handler.Requests.Count;
        var outputs = new Dictionary<string, JsonElement> { ["chosen"] = JsonSerializer.SerializeToElement("left") };

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Assess", RunStatus.Failed, "boom", CancellationToken.None,
            outputs: outputs, decision: Guid.NewGuid());

        var change = handler.Requests.Skip(writtenBefore).Should().ContainSingle().Subject;
        change.RequestUri!.AbsolutePath.Should().Be("/api/model/fragment");
        (await ReadJson(change)).GetProperty("relationships").EnumerateArray().Should().BeEmpty();
        var nodeRunId = (await CreatedThings(handler)).First().GetProperty("id").GetGuid();
        (await PropertiesWrittenSinceCreation(handler, nodeRunId)).Select(written => written.Name).Should().BeEquivalentTo(
            [ModelNames.Status, ModelNames.Error, ModelNames.FinishedUtc, ModelNames.Outputs, ModelNames.Decision]);
    }

    [Fact]
    public async Task SetRunStatusAsync_WritesTheWholeChangeInOneRequest()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);
        var writtenBefore = handler.Requests.Count;

        await gateway.SetRunStatusAsync(runId, RunStatus.Failed, CancellationToken.None, "boom");

        handler.Requests.Skip(writtenBefore).Where(request => request.RequestUri!.AbsolutePath == "/api/model/fragment")
            .Should().ContainSingle();
        (await PropertiesWrittenSinceCreation(handler, runId)).Select(written => written.Name).Should().BeEquivalentTo(
            [ModelNames.Status, ModelNames.Error, ModelNames.FinishedUtc]);
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

        (await CreatedThings(handler)).Select(nodeRun => nodeRun.GetProperty("name").GetString())
            .Should().BeEquivalentTo(["NodeRun Echo #0", "NodeRun Echo #1"]);
    }

    [Fact]
    public async Task SetRunStatusAsync_WritesTheStatusOnTheRun()
    {
        var runId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);

        var written = await PropertiesWrittenSinceCreation(handler, runId);
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

        var properties = (await CreatedThings(handler)).Single().GetProperty("properties");
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

        var nodeRunId = (await CreatedThings(handler))
            .Select(body => body.GetProperty("id").GetGuid()).Single(id => id != runId);
        var nodeRunWritten = await PropertiesWrittenSinceCreation(handler, nodeRunId);
        nodeRunWritten.Should().Contain((ModelNames.Outputs, """{"notice":"sent"}"""));
        nodeRunWritten.Select(w => w.Name).Should().NotContain(ModelNames.FinishedUtc);
        (await PropertiesWrittenSinceCreation(handler, runId)).Select(w => w.Name).Should().NotContain(ModelNames.FinishedUtc);
    }

    [Fact]
    public async Task Trying_between_attempts_is_not_a_finish_for_a_node_run()
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (gateway, handler) = NewGateway();
        await gateway.CreateRunAsync(runId, Guid.NewGuid(), CancellationToken.None);

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Trying, "the service was away", CancellationToken.None);

        var nodeRunId = (await CreatedThings(handler))
            .Select(body => body.GetProperty("id").GetGuid()).Single(id => id != runId);
        var nodeRunWritten = await PropertiesWrittenSinceCreation(handler, nodeRunId);
        nodeRunWritten.Should().Contain((ModelNames.Status, RunStatus.Trying));
        nodeRunWritten.Select(w => w.Name).Should().NotContain(ModelNames.FinishedUtc);
    }

    [Fact]
    public async Task PlaceVigilAsync_AsksForAVigilNamingTheResumptionConnection_AndAnswersItsIdentifier()
    {
        var subjectId = Guid.NewGuid();
        var rangeId = Guid.NewGuid();
        var vigilId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/vigils"
            ? Json(HttpStatusCode.Created, $$"""{"Id":"{{vigilId}}","Name":"Plot 7 enters decided","CallState":null}""")
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

    [Fact]
    public async Task IsStopAsync_IsTrueOnlyForARelationshipWrittenAlongTheConnectionTheRunStoppingRoleBinds()
    {
        var stopsConnection = Guid.NewGuid();
        var alongStops = Guid.NewGuid();
        var alongAnother = Guid.NewGuid();
        var (gateway, _) = NewGateway(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(alongStops.ToString(), StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $$"""{"Id":"{{alongStops}}","PredicateId":"{{stopsConnection}}"}""");
            if (path.EndsWith(alongAnother.ToString(), StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, $$"""{"Id":"{{alongAnother}}","PredicateId":"{{Guid.NewGuid()}}"}""");
            if (path == "/api/snapshots" && request.Method == HttpMethod.Post)
                return BoundSnapshot(PipelineRoles.RunStopping, stopsConnection);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        (await gateway.IsStopAsync(alongStops, CancellationToken.None)).Should().BeTrue();
        (await gateway.IsStopAsync(alongAnother, CancellationToken.None)).Should().BeFalse();
        (await gateway.IsStopAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task StopRequestedAsync_ReadsTheReasonAndTheRunsStillGoingAboutTheTarget()
    {
        var request = Guid.NewGuid();
        var plot = Guid.NewGuid();
        var waitingRun = Guid.NewGuid();
        var finishedRun = Guid.NewGuid();
        var runArchetype = Guid.NewGuid();
        var about = Guid.NewGuid();
        var binds = Guid.NewGuid();
        var stopKind = Guid.NewGuid();
        var isPredicate = Guid.NewGuid();
        object Thing(Guid id, string name, object properties, object? overrides = null) =>
            new { id, name, isArchetype = false, properties, inheritedOverrides = overrides, states = Array.Empty<string>() };
        object Edge(Guid subject, Guid predicate, Guid target) =>
            new { id = Guid.NewGuid(), subjectId = subject, predicateId = predicate, targetId = target, properties = new { } };
        var snapshot = JsonSerializer.Serialize(new
        {
            snapshot = new
            {
                things = new[]
                {
                    Thing(request, "Stop request", new { }, new Dictionary<string, object>
                    {
                        [stopKind.ToString()] = new { sourceName = "StopRequest", properties = new { reason = new { value = "site withdrawn", typeInfo = "vos.String" } } },
                    }),
                    Thing(plot, "Plot 7", new { }),
                    Thing(waitingRun, "PipelineRun a", new { status = new { value = RunStatus.Waiting, typeInfo = "vos.String" } }),
                    Thing(finishedRun, "PipelineRun b", new { status = new { value = RunStatus.Succeeded, typeInfo = "vos.String" } }),
                    Thing(binds, ModelNames.Binds, new { }),
                    Thing(PipelineRoles.RunSubject.Id, "runSubject role", new { }),
                    Thing(PipelineRoles.PipelineRun.Id, "pipelineRun role", new { }),
                    Thing(about, "about", new { }),
                    Thing(runArchetype, "PipelineRun", new { }),
                    Thing(isPredicate, ModelNames.Is, new { }),
                },
                relationships = new[]
                {
                    Edge(PipelineRoles.RunSubject.Id, binds, about),
                    Edge(PipelineRoles.PipelineRun.Id, binds, runArchetype),
                    Edge(waitingRun, about, plot),
                    Edge(finishedRun, about, plot),
                    Edge(waitingRun, isPredicate, runArchetype),
                },
            },
        });
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.OK, snapshot));

        var stop = await gateway.StopRequestedAsync(request, plot, CancellationToken.None);

        stop.Should().BeEquivalentTo(new StopRequested(request, "site withdrawn", [waitingRun]));
        (await gateway.StopRequestedAsync(request, waitingRun, CancellationToken.None)).Runs.Should().Equal(waitingRun);
    }

    [Fact]
    public async Task RetireVigilAsync_DeletesTheVigil_AndTakesOneAlreadyGoneAsDone()
    {
        var vigil = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var (gateway, handler) = NewGateway(request => request.RequestUri!.AbsolutePath.EndsWith(gone.ToString(), StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        await gateway.RetireVigilAsync(vigil, CancellationToken.None);
        await gateway.RetireVigilAsync(gone, CancellationToken.None);

        RequestsTo(handler, $"/api/vigils/{vigil}", HttpMethod.Delete).Should().ContainSingle();
    }

    [Fact]
    public async Task PlaceVigilAsync_WhenThePlatformRefusesWithoutAReason_AnswersTheStatus()
    {
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/vigils"
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("not a reason") }
            : RespondLikeAModelThatBindsItsRoles(request));

        var placement = await gateway.PlaceVigilAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        placement.Should().Be(new VigilPlacement.Refused("HTTP 400"));
    }

    [Fact]
    public async Task ReadRunRecordAsync_ReadsANodeRunWhoseOutputsAreNotJsonAsOneWithNone_AndTakesOnlyNumberedAttemptsFromWhatItHolds()
    {
        var runId = Guid.NewGuid();
        var pipelineId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var nodeRunId = Guid.NewGuid();
        var hasId = Guid.NewGuid();
        var ofId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var notAnAttemptId = Guid.NewGuid();
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, $$$"""
                {"snapshot":{"things":[
                  {"id":"{{{runId}}}","name":"PipelineRun","properties":{"parameters":{"typeInfo":"vos.String","value":"{}"} } },
                  {"id":"{{{pipelineId}}}","name":"Review a submission","properties":{}},
                  {"id":"{{{nodeRunId}}}","name":"NodeRun Review","properties":{
                    "nodeId":{"typeInfo":"vos.String","value":"{{{nodeId}}}"},
                    "status":{"typeInfo":"vos.String","value":"succeeded"},
                    "outputs":{"typeInfo":"vos.String","value":"not an object"} } },
                  {"id":"{{{attemptId}}}","name":"Attempt 1","properties":{
                    "number":{"typeInfo":"vos.String","value":"1"},
                    "startedUtc":{"typeInfo":"vos.String","value":"2026-10-15T12:00:00.0000000+00:00"},
                    "finishedUtc":{"typeInfo":"vos.String","value":"2026-10-15T12:00:01.0000000+00:00"},
                    "status":{"typeInfo":"vos.String","value":"succeeded"} } },
                  {"id":"{{{notAnAttemptId}}}","name":"a note","properties":{} },
                  {"id":"{{{hasId}}}","name":"has","properties":{}},
                  {"id":"{{{ofId}}}","name":"of","properties":{}}],
                 "relationships":[
                  {"id":"{{{Guid.NewGuid()}}}","subjectId":"{{{runId}}}","predicateId":"{{{hasId}}}","targetId":"{{{nodeRunId}}}","properties":{}},
                  {"id":"{{{Guid.NewGuid()}}}","subjectId":"{{{nodeRunId}}}","predicateId":"{{{hasId}}}","targetId":"{{{attemptId}}}","properties":{}},
                  {"id":"{{{Guid.NewGuid()}}}","subjectId":"{{{nodeRunId}}}","predicateId":"{{{hasId}}}","targetId":"{{{notAnAttemptId}}}","properties":{}},
                  {"id":"{{{Guid.NewGuid()}}}","subjectId":"{{{runId}}}","predicateId":"{{{ofId}}}","targetId":"{{{pipelineId}}}","properties":{}}]}}
                """)
            : RespondLikeAModelThatBindsItsRoles(request));

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);

        record!.PipelineId.Should().Be(pipelineId);
        record.Nodes[nodeId].Status.Should().Be("succeeded");
        record.Nodes[nodeId].Outputs.Should().BeEmpty();
        record.Nodes[nodeId].AttemptsMade.Should().ContainSingle().Which.Number.Should().Be(1);
    }

    [Fact]
    public async Task A_run_record_holds_each_nodes_error_and_decision_and_each_items_own_record_with_its_number_and_the_first_count()
    {
        var runId = Guid.NewGuid();
        var pipelineId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var (aggregate, first, second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var accept = Guid.NewGuid();
        var hasId = Guid.NewGuid();
        var ofId = Guid.NewGuid();
        static object Text(string value) => new { typeInfo = "vos.String", value };
        object NodeRun(Guid id, string status, string? index, string? error, string? decision = null)
        {
            var properties = new Dictionary<string, object> { ["nodeId"] = Text(nodeId.ToString()), ["status"] = Text(status) };
            if (index is not null) (properties["index"], properties["total"]) = (Text(index), Text("3"));
            if (error is not null) properties["error"] = Text(error);
            if (decision is not null) properties["decision"] = Text(decision);
            return new { id, name = "NodeRun Check", properties };
        }
        object Relates(Guid subject, Guid predicate, Guid target) =>
            new { id = Guid.NewGuid(), subjectId = subject, predicateId = predicate, targetId = target, properties = new { } };
        var snapshot = JsonSerializer.Serialize(new
        {
            snapshot = new
            {
                things = new[]
                {
                    new { id = runId, name = "PipelineRun", properties = (object)new { parameters = Text("{}") } },
                    new { id = pipelineId, name = "Reservoir check", properties = (object)new { } },
                    new { id = hasId, name = "has", properties = (object)new { } },
                    new { id = ofId, name = "of", properties = (object)new { } },
                }.Cast<object>().Concat([NodeRun(aggregate, "partial", null, "1/2 items failed"), NodeRun(second, "failed", "2", "east is dry"), NodeRun(first, "succeeded", "0", null, accept.ToString())]),
                relationships = new[] { Relates(runId, hasId, aggregate), Relates(runId, hasId, second), Relates(runId, hasId, first), Relates(runId, ofId, pipelineId) },
            },
        });
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/snapshots" && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, snapshot)
            : RespondLikeAModelThatBindsItsRoles(request));

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);

        var check = record!.Nodes.Should().ContainSingle().Which.Value;
        check.Error.Should().Be("1/2 items failed");
        check.Items!.Select(item => (item.Status, item.Error, item.Decision, item.Number))
            .Should().Equal(("succeeded", null, (Guid?)accept, new ItemNumber(0, 3)), ("failed", "east is dry", null, new ItemNumber(2, 3)));
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

        var nodeRunId = (await CreatedThings(handler)).Single().GetProperty("id").GetGuid();
        var written = await PropertiesWrittenSinceCreation(handler, nodeRunId);
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

        var properties = (await CreatedThings(handler)).Single().GetProperty("properties");
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
        var nodeRunId = (await CreatedThings(handler)).Single().GetProperty("id").GetGuid();

        await gateway.RelateProducedAsync(runId, nodeId, null, [one, two], CancellationToken.None);

        var related = await CreatedRelationships(handler);
        related.Should().Contain((nodeRunId, ProducedPredicateId, one)).And.Contain((nodeRunId, ProducedPredicateId, two));
    }

    [Fact]
    public async Task RelateProducedAsync_WhenTheModelMarksNoPredicate_WritesNothing()
    {
        var (gateway, handler) = NewGateway(request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var relating = () => gateway.RelateProducedAsync(Guid.NewGuid(), Guid.NewGuid(), null, [Guid.NewGuid()], CancellationToken.None);

        await relating.Should().NotThrowAsync();
        RequestsTo(handler, "/api/relationships", HttpMethod.Post).Should().BeEmpty();
    }

    private static readonly DateTimeOffset Nine = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static string SubscriptionAnswer(Guid subscriptionId, params object[] things) =>
        JsonSerializer.Serialize(new { subscriptionId, watermark = 12, snapshot = new { things, relationships = Array.Empty<object>() } });

    private static object ACadenceStatingItsOwnValues(Guid id, string name, object? interval, string? lastDue = null) => new
    {
        id, name, isArchetype = false, states = Array.Empty<string>(),
        properties = new Dictionary<string, object>
        {
            [ModelNames.IntervalMinutes] = new { value = interval, typeInfo = "vos.Double" },
            [ModelNames.LastDue] = new { value = lastDue, typeInfo = "vos.DateTime" },
        },
    };

    private sealed class StreamThatNeverAnswers : Stream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<int>().Task);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            new TaskCompletionSource<int>().Task;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A stream the stand-in broker keeps open: the test sends each frame, and ends it when the case is about a drop.
    private sealed class OpenStream
    {
        private readonly System.IO.Pipelines.Pipe _pipe = new();

        public HttpResponseMessage Response()
        {
            var content = new StreamContent(_pipe.Reader.AsStream());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        public async Task SendAsync(string frame)
        {
            await _pipe.Writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(frame));
            await _pipe.Writer.FlushAsync();
        }

        public Task EndAsync() => _pipe.Writer.CompleteAsync().AsTask();
    }

    private static string PropertyChangedFrame(Guid cadenceId, string property, object? value) =>
        $"id: 13\nevent: PropertyChanged\ndata: {JsonSerializer.Serialize(new { Kind = "PropertyChanged", EntityId = cadenceId, PropertyName = property, Value = value })}\n\n";

    private static string ThingMovedFrame(string kind, Guid thingId, object? thing = null) =>
        $"event: {kind}\ndata: {JsonSerializer.Serialize(new { Kind = kind, EntityId = thingId, Thing = thing })}\n\n";

    private static IReadOnlyList<Cadence> Held(MyceliumGateway gateway) =>
        gateway.CadencesAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static (MyceliumGateway Gateway, MockHttpMessageHandler Handler) ABrokerHoldingCadences(
        OpenStream stream, params object[] cadences)
    {
        var subscriptionId = Guid.NewGuid();
        return NewGateway(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/subscriptions" when request.Method == HttpMethod.Post => Json(HttpStatusCode.OK, SubscriptionAnswer(subscriptionId, cadences)),
            var path when path == $"/api/subscriptions/{subscriptionId}/stream" => stream.Response(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, cadenceStreamRetry: TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task CadencesAsync_OpensOneSubscriptionOnTheCadenceKindWithLaterMatches_AndReadsEachCadenceButNotItsKind()
    {
        var kind = Guid.NewGuid();
        var hourly = Guid.NewGuid();
        var daily = Guid.NewGuid();
        var unset = Guid.NewGuid();
        var stream = new OpenStream();
        var (gateway, handler) = ABrokerHoldingCadences(stream,
            new { id = kind, name = "Cadence", isArchetype = true, properties = new Dictionary<string, object>(), states = Array.Empty<string>() },
            new
            {
                id = hourly, name = "Every hour", isArchetype = false, states = Array.Empty<string>(),
                properties = new Dictionary<string, object>(),
                inheritedOverrides = new Dictionary<string, object>
                {
                    [kind.ToString()] = new
                    {
                        sourceName = "Cadence",
                        properties = new Dictionary<string, object>
                        {
                            [ModelNames.IntervalMinutes] = new { value = 60, typeInfo = "vos.Double" },
                            [ModelNames.LastDue] = new { value = "2026-10-15T09:00:00.0000000+00:00", typeInfo = "vos.DateTime" },
                        },
                    },
                },
            },
            new
            {
                id = daily, name = "Every day", isArchetype = false, states = Array.Empty<string>(),
                properties = new Dictionary<string, object> { [ModelNames.IntervalMinutes] = new { value = "1440", typeInfo = "vos.String" } },
            },
            ACadenceStatingItsOwnValues(unset, "Not yet set", null));
        await using var _ = gateway;

        var cadences = await gateway.CadencesAsync(CancellationToken.None);
        var again = await gateway.CadencesAsync(CancellationToken.None);

        cadences.Should().BeEquivalentTo(new[]
        {
            new Cadence(hourly, "Every hour", 60, Nine),
            new Cadence(daily, "Every day", 1440, null),
            new Cadence(unset, "Not yet set", 0, null),
        });
        again.Should().BeEquivalentTo(cadences);
        var opened = await ReadJson(RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Single());
        opened.GetProperty("boundTypes").EnumerateArray().Single().GetGuid().Should().Be(PipelineRoles.Cadence.Id);
        opened.GetProperty("includeLaterMatches").GetBoolean().Should().BeTrue("a cadence made after the subscription opened is a cadence too");
        opened.GetProperty("includeIsAncestors").GetBoolean().Should().BeFalse();
        opened.GetProperty("includeRelationships").GetBoolean().Should().BeFalse();
        RequestsTo(handler, "/api/snapshots").Should().BeEmpty("a look reads nothing from the broker");
    }

    [Fact]
    public async Task A_cadence_edited_on_the_stream_is_due_at_its_new_interval_without_a_read()
    {
        var hourly = Guid.NewGuid();
        var stream = new OpenStream();
        var (gateway, handler) = ABrokerHoldingCadences(stream, ACadenceStatingItsOwnValues(hourly, "Every hour", 60, Nine.ToString("o")));
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Single().IntervalMinutes.Should().Be(60);

        await stream.SendAsync(PropertyChangedFrame(hourly, ModelNames.IntervalMinutes, 30));
        await Settle.UntilAsync(() => Held(gateway).Single().IntervalMinutes == 30, "the edited interval reaches the held cadence");

        CadenceSchedule.DueAt(Held(gateway).Single(), Nine.AddMinutes(31), Nine.AddMinutes(29)).Should().Equal(Nine.AddMinutes(30));
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}")
            .Should().OnlyContain(call => call.StartsWith("POST /api/subscriptions") || call.StartsWith("GET /api/subscriptions/"));
        RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Should().ContainSingle();
    }

    [Fact]
    public async Task A_cadence_typed_into_the_kind_later_is_held_and_one_typed_out_or_deleted_is_let_go()
    {
        var hourly = Guid.NewGuid();
        var daily = Guid.NewGuid();
        var stream = new OpenStream();
        var (gateway, _) = ABrokerHoldingCadences(stream, ACadenceStatingItsOwnValues(hourly, "Every hour", 60));
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Should().ContainSingle();

        await stream.SendAsync(ThingMovedFrame("ThingEntered", daily, ACadenceStatingItsOwnValues(daily, "Every day", 1440)));
        await Settle.UntilAsync(() => Held(gateway).Count == 2, "the cadence typed in later is held");
        Held(gateway).Should().Contain(new Cadence(daily, "Every day", 1440, null));

        await stream.SendAsync(ThingMovedFrame("ThingLeft", daily));
        await Settle.UntilAsync(() => Held(gateway).Count == 1, "the cadence typed out is let go");
        await stream.SendAsync(ThingMovedFrame("ThingDeleted", hourly));
        await Settle.UntilAsync(() => Held(gateway).Count == 0, "the deleted cadence is let go");
    }

    [Fact]
    public async Task A_dropped_cadence_stream_is_subscribed_again_and_the_cadences_read_again()
    {
        var hourly = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstStream = new OpenStream();
        var secondStream = new OpenStream();
        var opened = 0;
        var (gateway, handler) = NewGateway(request => (request.RequestUri!.AbsolutePath, request.Method.Method) switch
        {
            ("/api/subscriptions", "POST") => Json(HttpStatusCode.OK, ++opened == 1
                ? SubscriptionAnswer(first, ACadenceStatingItsOwnValues(hourly, "Every hour", 60))
                : SubscriptionAnswer(second, ACadenceStatingItsOwnValues(hourly, "Every hour", 30))),
            (var path, "GET") when path == $"/api/subscriptions/{first}/stream" => firstStream.Response(),
            (var path, "GET") when path == $"/api/subscriptions/{second}/stream" => secondStream.Response(),
            (_, "DELETE") => throw new HttpRequestException("the broker that held the subscription is gone"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, cadenceStreamRetry: TimeSpan.FromMilliseconds(10));
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Single().IntervalMinutes.Should().Be(60);

        await firstStream.EndAsync();
        await Settle.UntilAsync(() => Held(gateway).Single().IntervalMinutes == 30, "the second subscription's snapshot replaces the first");

        RequestsTo(handler, "/api/subscriptions", HttpMethod.Post).Should().HaveCount(2);
        handler.Requests.Where(request => request.Method == HttpMethod.Delete).Select(request => request.RequestUri!.AbsolutePath)
            .Should().Equal($"/api/subscriptions/{first}");
    }

    [Fact]
    public async Task A_frame_that_is_not_a_change_is_passed_over_and_the_next_one_applied()
    {
        var hourly = Guid.NewGuid();
        var stream = new OpenStream();
        var (gateway, _) = ABrokerHoldingCadences(stream, ACadenceStatingItsOwnValues(hourly, "Every hour", 60));
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Should().ContainSingle();

        await stream.SendAsync("event: PropertyChanged\ndata: not a change\n\n");
        await stream.SendAsync("data: {\"EntityId\":\"" + hourly + "\"}\n\n");
        await stream.SendAsync(PropertyChangedFrame(hourly, ModelNames.IntervalMinutes, 15));

        await Settle.UntilAsync(() => Held(gateway).Single().IntervalMinutes == 15, "the change after the unreadable frames is applied");
    }

    [Fact]
    public async Task Disposing_the_gateway_while_no_subscription_can_be_opened_stops_the_following()
    {
        var (gateway, handler) = NewGateway(request => request.RequestUri!.AbsolutePath == "/api/subscriptions"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.NotFound), cadenceStreamRetry: TimeSpan.FromMilliseconds(10));
        var looking = () => gateway.CadencesAsync(CancellationToken.None);
        await looking.Should().ThrowAsync<HttpRequestException>();

        await Settle.ForAsync(gateway.DisposeAsync().AsTask(), "disposing waits for the following to stop");
        var asked = handler.Requests.Count;
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));

        handler.Requests.Count.Should().Be(asked, "nothing subscribes after the gateway is disposed");
    }

    [Fact]
    public async Task Disposing_the_gateway_while_the_cadence_stream_ignores_the_stop_gives_up_after_its_wait()
    {
        var subscriptionId = Guid.NewGuid();
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/subscriptions" when request.Method == HttpMethod.Post => Json(HttpStatusCode.OK, SubscriptionAnswer(subscriptionId)),
            var path when path == $"/api/subscriptions/{subscriptionId}/stream" =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StreamThatNeverAnswers()) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, disposeWait: TimeSpan.FromMilliseconds(50));
        (await gateway.CadencesAsync(CancellationToken.None)).Should().BeEmpty();

        await Settle.ForAsync(gateway.DisposeAsync().AsTask(), "disposing gives up on a stream that never answers the stop");
    }

    [Fact]
    public async Task The_instant_a_cadence_is_marked_due_moves_the_held_cadence_before_the_stream_carries_it()
    {
        var hourly = Guid.NewGuid();
        var noon = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var stream = new OpenStream();
        var subscriptionId = Guid.NewGuid();
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/subscriptions" when request.Method == HttpMethod.Post =>
                Json(HttpStatusCode.OK, SubscriptionAnswer(subscriptionId, ACadenceStatingItsOwnValues(hourly, "Every hour", 60))),
            var path when path == $"/api/subscriptions/{subscriptionId}/stream" => stream.Response(),
            var path when path == $"/api/things/{hourly}/properties" => new HttpResponseMessage(HttpStatusCode.OK),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Single().LastDue.Should().BeNull();

        await gateway.SetLastDueAsync(hourly, noon, CancellationToken.None);

        (await gateway.CadencesAsync(CancellationToken.None)).Single().LastDue.Should().Be(noon);
    }

    [Fact]
    public async Task CadencesAsync_WhileNoSubscriptionCanBeOpened_Fails_AndAnswersOnceOneOpens()
    {
        var hourly = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var stream = new OpenStream();
        var brokerIsAway = true;
        var (gateway, _) = NewGateway(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/subscriptions" when request.Method == HttpMethod.Post => brokerIsAway
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, SubscriptionAnswer(subscriptionId, ACadenceStatingItsOwnValues(hourly, "Every hour", 60))),
            var path when path == $"/api/subscriptions/{subscriptionId}/stream" => stream.Response(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }, cadenceStreamRetry: TimeSpan.FromMilliseconds(10));
        await using var _ = gateway;

        var looking = () => gateway.CadencesAsync(CancellationToken.None);
        await looking.Should().ThrowAsync<HttpRequestException>();
        await looking.Should().ThrowAsync<HttpRequestException>("the failure stands until a subscription opens");

        brokerIsAway = false;
        await Settle.UntilAsync(() => gateway.CadencesAsync(CancellationToken.None).IsCompletedSuccessfully, "the next attempt opens a subscription");
        (await gateway.CadencesAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task SetLastDueAsync_WritesTheInstantAsADateTime()
    {
        var cadenceId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.SetLastDueAsync(cadenceId, new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);

        var body = await ReadJson(RequestsTo(handler, $"/api/things/{cadenceId}/properties", HttpMethod.Put).Single());
        body.GetProperty("name").GetString().Should().Be(ModelNames.LastDue);
        body.GetProperty("type").GetString().Should().Be("vos.DateTime");
        DateTimeOffset.Parse(body.GetProperty("value").GetString()!).Should().Be(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
    }

    private static async Task<List<(string Name, string? Value)>> PropertiesWrittenSinceCreation(MockHttpMessageHandler handler, Guid thingId)
    {
        var written = new List<(string Name, string? Value)>();
        var created = false;
        foreach (var request in handler.Requests)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == $"/api/things/{thingId}/properties")
            {
                var body = await ReadJson(request);
                written.Add((body.GetProperty("name").GetString()!, body.GetProperty("value").GetString()));
            }
            if (request.Method != HttpMethod.Post || path != "/api/model/fragment") continue;

            var things = (await ReadJson(request)).GetProperty("things").EnumerateArray();
            foreach (var thing in things.Where(thing => thing.GetProperty("id").GetGuid() == thingId))
            {
                if (created)
                    written.AddRange(thing.GetProperty("properties").EnumerateObject()
                        .Select(property => (property.Name, property.Value.GetProperty("value").GetString())));
                created = true;
            }
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
        var written = await PropertiesWrittenSinceCreation(handler, runId);
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
    public async Task SubjectAsync_ReadsTheThingAndAnswersItsIdentifierAndName()
    {
        var thingId = Guid.NewGuid();
        var (gateway, handler) = NewGateway(_ => Json(HttpStatusCode.OK,
            $$$"""{"Id":"{{{thingId}}}","Name":"Reservoir 7","Properties":{}}"""));

        (await gateway.SubjectAsync(thingId, CancellationToken.None)).Should().Be(new RunSubject(thingId, "Reservoir 7"));
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/api/things/{thingId}");
    }

    [Fact]
    public async Task SubjectAsync_WhenTheModelDoesNotHoldTheThing_AnswersNone()
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await gateway.SubjectAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    // A broker that cannot answer has not said the Thing is missing, so the start is not refused as naming nothing.
    [Fact]
    public async Task SubjectAsync_WhenTheBrokerFails_Throws()
    {
        var (gateway, _) = NewGateway(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await gateway.Invoking(g => g.SubjectAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ServiceCallAsync_SendsTheEnvelopeToTheNamedEndpointAndReturnsWhatItSaid()
    {
        var (gateway, handler) = NewGateway(_ => Json(HttpStatusCode.Accepted, """{"outputs":{"sum":3}}"""));
        var envelope = JsonDocument.Parse("""{"runId":"r","nodeId":"n"}""").RootElement;

        var result = await gateway.CallAsync("energy balance", envelope, CancellationToken.None);

        result.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        result.Body.Should().Contain("\"sum\":3");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/endpoints/energy%20balance");
    }

    [Fact]
    public async Task ServiceCallAsync_WhenTheEndpointFails_ReportsTheStatusRatherThanThrowing()
    {
        var (gateway, _) = NewGateway(_ => Json(HttpStatusCode.BadGateway, "upstream is down"));
        var envelope = JsonDocument.Parse("{}").RootElement;

        var result = await gateway.CallAsync("echo", envelope, CancellationToken.None);

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
        foreach (var request in RequestsTo(handler, "/api/snapshots", HttpMethod.Post))
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

        var lookups = await Task.WhenAll(RequestsTo(handler, "/api/snapshots", HttpMethod.Post).Select(ReadJson));
        lookups.Where(asked => asked.TryGetProperty("boundTypes", out _)).Should().BeEmpty();
        var lookup = lookups.Single(asked => asked.GetProperty("ids").EnumerateArray().Any(id => id.GetGuid() == PipelineRoles.PipelineRun.Id));
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
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json(HttpStatusCode.OK, """{"snapshot":{"things":[],"relationships":[]}}""")
                : new HttpResponseMessage(HttpStatusCode.OK));

        var creating = () => gateway.CreateRunAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        (await creating.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*role {PipelineRoles.PipelineRun.Word}*");
    }

    private static async Task<JsonElement> ReadJson(HttpRequestMessage request) =>
        JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync());
}
