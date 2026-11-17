using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The relate box's unit tests answer every read and write themselves, so only these show that the engine brings the
// connection's service and state back with the drawing, takes the relationship, calls the service and answers the wait.
public sealed class PhloemRelatesAThingAlongAConnectionTests : IClassFixture<TheEngine>, IAsyncLifetime
{
    private const string WorkedOutAt = "workedOutAt";
    private const string WorkedOut = "WorkedOut";
    private const string RelateThingIn = "relate.thing.in";
    private const string RelateThingOut = "relate.thing.out";

    private readonly TheEngine _engine;
    private readonly ServiceStandIn _service = new();

    public PhloemRelatesAThingAlongAConnectionTests(TheEngine engine) => _engine = engine;

    public Task InitializeAsync() => _service.StartAsync();

    public Task DisposeAsync() => _service.DisposeAsync().AsTask();

    [FactAgainstTheEngine]
    public async Task A_run_reaching_a_relate_box_writes_one_relationship_to_the_connections_service_and_the_platform_calls_the_service()
    {
        var drawing = await DrawAsync();
        var study = await AStudyAsync(drawing);

        var run = await RunAsync(drawing, study);

        var relate = run.Nodes.Should().ContainSingle(node => node.NodeId == drawing.Relate, run.Error).Subject;
        relate.Status.Should().Be(RunStatus.Waiting, relate.Error);
        relate.Outputs![ModelNames.Written].GetBoolean().Should().BeTrue();
        (await ReachedAlongTheConnectionAsync(drawing, study)).Should().Equal(drawing.Service);
        await Settle.UntilAsync(() => _service.CalledAbout(study), "the platform calls the connection's service for the study");
    }

    [FactAgainstTheEngine]
    public async Task A_second_run_for_the_same_Thing_finds_the_relationship_and_writes_none()
    {
        var drawing = await DrawAsync();
        var study = await AStudyAsync(drawing);
        await RunAsync(drawing, study);
        var transport = new RecordingWritesAlong(drawing.Connection, _engine.Transport);

        var second = await RunAsync(drawing, study, transport);

        var relate = second.Nodes.Should().ContainSingle(node => node.NodeId == drawing.Relate, second.Error).Subject;
        relate.Status.Should().Be(RunStatus.Waiting, relate.Error);
        relate.Outputs![ModelNames.Written].GetBoolean().Should().BeFalse();
        transport.Writes.Should().Be(0, "the read before the write finds the relationship, so the engine is asked for nothing it would refuse");
        (await ReachedAlongTheConnectionAsync(drawing, study)).Should().Equal(drawing.Service);
    }

    [FactAgainstTheEngine]
    public async Task The_box_waits_for_the_state_its_connection_is_proven_by_and_the_platform_answers_once_the_Thing_enters_it()
    {
        var drawing = await DrawAsync();
        var study = await AStudyAsync(drawing);

        var run = await RunAsync(drawing, study);

        var record = await Gateway().ReadRunRecordAsync(run.RunId, CancellationToken.None);
        record!.Nodes[drawing.Relate].Watching.Should().Be(new Watch(study, drawing.WorkedOut));
        (await WaitOfAsync(run.RunId))!.Answer.Should().Be(WaitAnswer.None);

        (await _engine.Admin.PostAsJsonAsync($"/api/things/{study}/properties",
            new { Name = WorkedOutAt, Type = "vos.DateTime", Value = DateTimeOffset.UtcNow })).EnsureSuccessStatusCode();

        await Settle.UntilAsync(async () => await WaitOfAsync(run.RunId) is { Answer: not WaitAnswer.None },
            "the platform answers the relate box once the study is worked out");
    }

    private sealed record Drawing(Guid Pipeline, Guid Relate, Guid Connection, Guid Service, Guid WorkedOut, Guid StudyKind);

    private MyceliumGateway Gateway(HttpMessageHandler? transport = null) => new(
        transport is null ? _engine.ClientFactory : new PerCallHttpClientFactory(transport),
        NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

    private async Task<PipelineRunResult> RunAsync(Drawing drawing, Guid study, HttpMessageHandler? transport = null) =>
        await new PipelineExecutor(Gateway(transport), NullLogger<PipelineExecutor>.Instance, new ModelClock())
            .RunAsync(drawing.Pipeline, JsonSerializer.SerializeToElement(new { }), CancellationToken.None,
                subject: new RunSubject(study, "study"));

    private async Task<IReadOnlyList<Guid>> ReachedAlongTheConnectionAsync(Drawing drawing, Guid study) =>
        (await Gateway().ReachedAlongAsync(study, drawing.Connection, CancellationToken.None)).Select(reached => reached.Id).ToList();

    private async Task<HeldWait?> WaitOfAsync(Guid runId) =>
        (await Gateway().WaitingNodeRunsAsync(CancellationToken.None)).SingleOrDefault(held => held.RunId == runId);

    private async Task<Guid> AStudyAsync(Drawing drawing)
    {
        var study = await _engine.DeclareAsync($"Study {Guid.NewGuid():N}");
        await _engine.RelateAsync(study, await _engine.TheOnlyThingNamedAsync(ModelNames.Is), drawing.StudyKind);
        (await _engine.Admin.PostAsJsonAsync($"/api/things/{study}/ranges", new { Name = WorkedOut, Criteria = $"{WorkedOutAt} IS KNOWN" }))
            .EnsureSuccessStatusCode();
        return study;
    }

    // The vocabulary and the kinds are written once per engine; each case draws its own pipeline and connection,
    // because each case has a service listening on a port of its own.
    private async Task<Drawing> DrawAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var @is = await _engine.TheOnlyThingNamedAsync(ModelNames.Is);
        var has = await _engine.TheOnlyThingNamedAsync(ModelNames.Has);
        var firstCase = await _engine.FindAsync("PipelineRelate") is null;

        var connectionKind = await PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection", isArchetype: true,
            Marked("__IsConnectionArchetype"));
        var serviceKind = await PlayingAsync(PipelineRoles.Service, "Service", isArchetype: true, Marked("__IsServiceArchetype"));
        var range = await PlayingAsync(RangeRole, "Range", isArchetype: true, Marked("__IsRangeArchetype"));
        var judges = await PlayingAsync(JudgedThingRole, "judges", properties: Marked("__IsJudgedThingPredicate"));
        await PlayingAsync(PipelineRoles.StateWatch, "watches", properties: Marked("__IsStateWatchPredicate"));
        var notifies = await PlayingAsync(NotifiedConnectionRole, "notifies", properties: Marked("__IsNotifiedConnectionPredicate"));
        await PlayingAsync(PipelineRoles.CallRecord, "handledBy");
        await PlayingAsync(PipelineRoles.AwaitedThing, "awaits");
        await PlayingAsync(PipelineRoles.RunSubject, "about");
        await PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        var nodeRun = await PlayingAsync(PipelineRoles.NodeRun, "NodeRun", isArchetype: true);
        await PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);
        var pipelineKind = await PlayingAsync(PipelineRoles.Pipeline, "Pipeline", isArchetype: true);
        var nodeKind = await PlayingAsync(PipelineRoles.PipelineNode, "PipelineNode", isArchetype: true);
        var inputKind = await PlayingAsync(PipelineRoles.PipelineInput, "PipelineInput", isArchetype: true);
        var outputKind = await PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput", isArchetype: true);
        var relateKind = await PlayingAsync(PipelineRoles.PipelineRelate, "PipelineRelate", isArchetype: true);
        var portKind = await PlayingAsync(PipelineRoles.Port, "Port", isArchetype: true);
        var wireKind = await PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire", isArchetype: true);
        var inputConnector = await PlayingAsync(PipelineRoles.InputConnector, "inputConnector");
        var outputConnector = await PlayingAsync(PipelineRoles.OutputConnector, "outputConnector");
        var takenOn = await PlayingAsync(PipelineRoles.TakenOn, "takenOn");
        var success = await PlayingAsync(PipelineRoles.Success, "success");
        await PlayingAsync(PipelineRoles.Fail, "fail");
        await PlayingAsync(PipelineRoles.ErrorPort, "error", properties: PortNamed("error", ModelNames.DirectionOut, "string"));
        await PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath", isArchetype: true);
        await PlayingAsync(PipelineRoles.ComparesWith, "comparesWith");
        var standsFor = await PlayingAsync(PipelineRoles.StandsFor, "standsFor");
        var provenBy = await PlayingAsync(PipelineRoles.CompletionRange, "provenBy");
        var carries = await _engine.DeclareOnceAsync("carries");

        async Task<Guid> PortAsync(Guid holder, string name, string direction, string? thingName = null)
        {
            var port = await _engine.DeclareAsync(thingName ?? $"{name}.{direction} {Guid.NewGuid():N}", properties: PortNamed(name, direction, "any"));
            await _engine.RelateAsync(port, @is, portKind);
            await _engine.RelateAsync(holder, has, port);
            return port;
        }

        if (firstCase)
        {
            await _engine.RelateAsync(inputKind, @is, nodeKind);
            await _engine.RelateAsync(outputKind, @is, nodeKind);
            await _engine.RelateAsync(relateKind, @is, nodeKind);
            await PortAsync(relateKind, ModelNames.Thing, ModelNames.DirectionIn, RelateThingIn);
            await PortAsync(relateKind, ModelNames.Thing, ModelNames.DirectionOut, RelateThingOut);
            await PortAsync(relateKind, ModelNames.Written, ModelNames.DirectionOut, "relate.written.out");

            var studyKind = await _engine.DeclareAsync("Study", isArchetype: true);
            var workedOut = await _engine.DeclareAsync(WorkedOut, properties: new Dictionary<string, object?> { ["Criteria"] = $"{WorkedOutAt} IS KNOWN" });
            await _engine.RelateAsync(workedOut, @is, range);
            await _engine.RelateAsync(workedOut, judges, studyKind);

            var orchestrator = await _engine.DeclareAsync("orchestrator service");
            await _engine.RelateAsync(orchestrator, @is, serviceKind);
            var resumesRun = await _engine.DeclareAsync("resumesRun");
            await _engine.RelateAsync(resumesRun, @is, connectionKind);
            await _engine.RelateAsync(resumesRun, has, orchestrator);
            await _engine.RelateAsync(nodeRun, notifies, resumesRun);
        }
        var studyArchetype = await _engine.TheOnlyThingNamedAsync("Study");
        var stateWorkedOut = await _engine.TheOnlyThingNamedAsync(WorkedOut);

        var service = await _engine.DeclareAsync($"works out service {Guid.NewGuid():N}", properties: new Dictionary<string, object?>
        {
            ["ExecutablePath"] = "the stand-in, already running",
            ["ServicePort"] = _service.Port.ToString(),
        });
        await _engine.RelateAsync(service, @is, serviceKind);
        var connection = await _engine.DeclareAsync($"worksOut {Guid.NewGuid():N}");
        await _engine.RelateAsync(connection, @is, connectionKind);
        await _engine.RelateAsync(connection, has, service);
        await _engine.RelateAsync(connection, provenBy, stateWorkedOut);

        var pipeline = await _engine.DeclareAsync($"Work a study out {Guid.NewGuid():N}",
            properties: new Dictionary<string, object?> { [ModelNames.Published] = true });
        await _engine.RelateAsync(pipeline, @is, pipelineKind);
        var start = await _engine.DeclareAsync($"A study is recorded {Guid.NewGuid():N}");
        await _engine.RelateAsync(start, @is, inputKind);
        await _engine.RelateAsync(pipeline, has, start);
        var subjectOut = await PortAsync(start, PipelineExecutor.SubjectParam, ModelNames.DirectionOut);
        var relate = await _engine.DeclareAsync($"Work it out {Guid.NewGuid():N}");
        await _engine.RelateAsync(relate, @is, relateKind);
        await _engine.RelateAsync(relate, standsFor, connection);
        await _engine.RelateAsync(pipeline, has, relate);
        var end = await _engine.DeclareAsync($"Worked out {Guid.NewGuid():N}");
        await _engine.RelateAsync(end, @is, outputKind);
        await _engine.RelateAsync(pipeline, has, end);
        var endIn = await PortAsync(end, ModelNames.Thing, ModelNames.DirectionIn);

        async Task WireAsync(Guid from, Guid outPort, Guid to, Guid inPort)
        {
            var wire = await _engine.DeclareAsync($"wire {Guid.NewGuid():N}");
            await _engine.RelateAsync(wire, @is, wireKind);
            await _engine.RelateAsync(from, has, wire);
            await _engine.RelateAsync(wire, carries, to);
            await _engine.RelateAsync(wire, inputConnector, outPort);
            await _engine.RelateAsync(wire, outputConnector, inPort);
            await _engine.RelateAsync(wire, takenOn, success);
        }
        await WireAsync(start, subjectOut, relate, await _engine.TheOnlyThingNamedAsync(RelateThingIn));
        await WireAsync(relate, await _engine.TheOnlyThingNamedAsync(RelateThingOut), end, endIn);

        return new Drawing(pipeline, relate, connection, service, stateWorkedOut, studyArchetype);
    }

    private async Task<Guid> PlayingAsync(Role role, string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var played = await _engine.DeclareOnceAsync(name, isArchetype, properties);
        if (await _engine.FindAsync($"{role.Word} role") is not null) return played;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
        return played;
    }

    private static IReadOnlyDictionary<string, object?> Marked(string mark) => new Dictionary<string, object?> { [mark] = true };

    private static Dictionary<string, object?> PortNamed(string name, string direction, string type) => new()
    {
        [ModelNames.PortName] = name, [ModelNames.Direction] = direction, [ModelNames.PortType] = type,
    };

    private static readonly Role RangeRole = new("range", new("c081de17-c325-55b7-99ca-16fb835ae545"));
    private static readonly Role JudgedThingRole = new("judgedThing", new("63d59765-a9b7-53b8-8a2b-0de3899add18"));
    private static readonly Role NotifiedConnectionRole = new("notifiedConnection", new("8ac302e3-3de9-51db-8960-65f33396838e"));

    // The run writes its own records as relationships too, so only a write along the connection is counted.
    private sealed class RecordingWritesAlong(Guid predicate, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _writes;

        public int Writes => Volatile.Read(ref _writes);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/relationships"
                && (await request.Content!.ReadAsStringAsync(cancellationToken)).Contains(predicate.ToString(), StringComparison.Ordinal))
                Interlocked.Increment(ref _writes);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ServiceStandIn : IAsyncDisposable
    {
        private readonly List<JsonElement> _calls = new();
        private WebApplication? _app;

        public int Port { get; } = FreeLoopbackPort();

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.Urls.Add($"http://127.0.0.1:{Port}");
            _app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
            _app.MapPost("/handle", async (HttpRequest request) =>
            {
                var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
                lock (_calls) _calls.Add(body);
                return Results.Ok(new { success = true, accepted = true });
            });
            await _app.StartAsync();
        }

        public bool CalledAbout(Guid subjectId)
        {
            lock (_calls)
                return _calls.Any(call => call.TryGetProperty("subjectId", out var subject) && subject.GetString() == subjectId.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null) await _app.DisposeAsync();
        }

        private static int FreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
