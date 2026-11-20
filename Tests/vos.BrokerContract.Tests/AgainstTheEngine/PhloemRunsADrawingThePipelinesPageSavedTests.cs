using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Tests.Shared;
using Xunit;
using static vos.BrokerContract.Tests.AgainstTheEngine.EngineArrangement;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The requests replayed are the ones vos.Trellis/src/pipeline/savedDrawing.test.ts records the Pipelines page sending.
public sealed class PhloemRunsADrawingThePipelinesPageSavedTests : IClassFixture<TheEngine>, IAsyncLifetime
{
    private const string RecordedAt = "recordedAt";
    private const string WorkedOutAt = "workedOutAt";
    private const string Recorded = "Recorded";
    private const string WorkedOut = "WorkedOut";
    private const int WaitsAtMostMinutes = 90;

    private readonly TheEngine _engine;
    private readonly ServiceStandIn _service = new();
    private readonly ServiceStandIn _orchestrator = new();

    public PhloemRunsADrawingThePipelinesPageSavedTests(TheEngine engine) => _engine = engine;

    public async Task InitializeAsync()
    {
        await _service.StartAsync();
        await _orchestrator.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _service.DisposeAsync();
        await _orchestrator.DisposeAsync();
    }

    [FactAgainstTheEngine]
    public async Task A_drawing_saved_by_the_page_waits_for_its_state_relates_its_subject_and_finishes()
    {
        var model = await TheModelTheDrawingNamesAsync();
        var saved = await ReplayTheSaveAsync(model, await _engine.EditorAsync());
        var study = await AStudyAsync(model);
        var executor = new PipelineExecutor(Gateway(), NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var started = DateTimeOffset.UtcNow;

        var run = await executor.RunAsync(saved.Pipeline, JsonSerializer.SerializeToElement(new { }), CancellationToken.None,
            subject: new RunSubject(study, "study"));

        run.Nodes.Should().ContainSingle(node => node.NodeId == saved.Wait, run.Error).Which.Status.Should().Be(RunStatus.Waiting);
        var wait = await WaitOfAsync(run.RunId);
        wait!.NodeId.Should().Be(saved.Wait);
        wait.Watching.AwaitedId.Should().Be(study);
        wait.Watching.RangeId.Should().Be(model["recorded"]);
        wait.Watching.Until.Should().BeCloseTo(started.AddMinutes(WaitsAtMostMinutes), TimeSpan.FromMinutes(2),
            "the limit the page saved is the one the run waits by");

        await WriteNowAsync(study, RecordedAt);
        await AnsweredAsync(run.RunId);
        // The platform's call along resumesRun reaches a stand-in rather than Phloem's /handle (Test Case 8536), so the answer it completes is read back as a starting orchestrator reads it.
        (await executor.RecoverWaitsAsync(CancellationToken.None)).Resumed.Should().Be(1);

        var relating = await Gateway().ReadRunRecordAsync(run.RunId, CancellationToken.None);
        relating!.Nodes[saved.Relate].Status.Should().Be(RunStatus.Waiting, relating.Nodes[saved.Relate].Error);
        relating.Nodes[saved.Relate].Outputs[ModelNames.Written].GetBoolean().Should().BeTrue();
        (await Gateway().ReachedAlongAsync(study, model["works-out"], CancellationToken.None)).Select(reached => reached.Id)
            .Should().Equal(model["works-out-service"]);
        await Settle.UntilAsync(() => _service.CalledAbout(study), "the platform calls the connection's service for the study");

        await WriteNowAsync(study, WorkedOutAt);
        await AnsweredAsync(run.RunId);
        (await executor.RecoverWaitsAsync(CancellationToken.None)).Resumed.Should().Be(1);

        var finished = await Gateway().ReadRunRecordAsync(run.RunId, CancellationToken.None);
        finished!.Status.Should().Be(RunStatus.Succeeded, string.Join("; ", finished.Nodes.Values.Select(node => node.Error)));
        finished.Nodes[saved.End].Status.Should().Be(RunStatus.Succeeded);
    }

    private sealed record SavedDrawing(Guid Pipeline, Guid Wait, Guid Relate, Guid End);

    private static readonly Regex Placeholder = new(@"^\{\{(model|new|created):([^}]+)\}\}$");

    private static readonly string FixtureFile = Path.Combine(AppContext.BaseDirectory, "AgainstTheEngine", "Fixtures",
        "the-pipelines-page-saves-a-wait-and-a-relate-box.json");

    private async Task<SavedDrawing> ReplayTheSaveAsync(IReadOnlyDictionary<string, Guid> model, HttpClient editor)
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(FixtureFile))!;
        fixture["references"]!.AsArray().Select(reference => reference!.GetValue<string>())
            .Should().BeSubsetOf(model.Keys, "every Thing the page's save names is declared here");

        var drawn = new Dictionary<string, Guid>();
        var created = new Dictionary<string, Guid>();
        string Resolve(string value)
        {
            var match = Placeholder.Match(value);
            if (!match.Success) return value;
            var key = match.Groups[2].Value;
            return (match.Groups[1].Value switch
            {
                "model" => model[key],
                "new" => drawn.TryGetValue(key, out var id) ? id : drawn[key] = Guid.NewGuid(),
                _ => created[key],
            }).ToString();
        }
        JsonNode? Resolved(JsonNode? node) => node switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(Resolve(text)),
            JsonObject entries => new JsonObject(entries.Select(entry => KeyValuePair.Create(entry.Key, Resolved(entry.Value)))),
            JsonArray items => new JsonArray(items.Select(Resolved).ToArray()),
            _ => node?.DeepClone(),
        };

        var fragment = fixture["requests"]![0]!["body"]!;
        foreach (var request in fixture["requests"]!.AsArray())
        {
            var path = string.Join('/', request!["path"]!.GetValue<string>().Split('/').Select(Resolve));
            var body = request["body"] is { } written ? Resolved(written)!.ToJsonString() : null;
            using var message = new HttpRequestMessage(new HttpMethod(request["method"]!.GetValue<string>()), path);
            if (body is not null) message.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var answered = await editor.SendAsync(message);
            answered.StatusCode.Should().Be(HttpStatusCode.OK, "an editor's {0} {1} was answered: {2}",
                message.Method, path, await answered.Content.ReadAsStringAsync());
            if (message.Method == HttpMethod.Post && path == "/api/things")
                created[(created.Count + 1).ToString()] = (await answered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetGuid();
        }

        Guid TheOneThatIs(string kind) => Guid.Parse(Resolve(fragment["Relationships"]!.AsArray()
            .Single(relationship => relationship!["Name"]!.GetValue<string>() == PlatformPredicates.Is
                && relationship["Target"]!.GetValue<string>() == $"{{{{model:{kind}}}}}")!["Subject"]!.GetValue<string>()));
        return new SavedDrawing(TheOneThatIs("arch-pipeline"), TheOneThatIs("arch-wait"), TheOneThatIs("arch-relate"), TheOneThatIs("arch-output"));
    }

    // Declared by the administrator, as the platform's seed would be; only the page's own save is an editor's.
    private async Task<IReadOnlyDictionary<string, Guid>> TheModelTheDrawingNamesAsync()
    {
        foreach (var builtIn in new[] { PlatformPredicates.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(builtIn);
        var @is = await _engine.TheOnlyThingNamedAsync(PlatformPredicates.Is);
        var has = await _engine.TheOnlyThingNamedAsync(ModelNames.Has);
        var model = new Dictionary<string, Guid> { ["is"] = @is, ["has"] = has };

        var connectionKind = model["arch-connection"] = await _engine.PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection", isArchetype: true,
            Marked("__IsConnectionArchetype"));
        var serviceKind = model["arch-service"] = await _engine.PlayingAsync(PipelineRoles.Service, "Service", isArchetype: true, Marked("__IsServiceArchetype"));
        var range = await _engine.PlayingAsync(RangeRole, "Range", isArchetype: true, Marked("__IsRangeArchetype"));
        var judges = await _engine.PlayingAsync(JudgedThingRole, "judges", properties: Marked("__IsJudgedThingPredicate"));
        await _engine.PlayingAsync(PipelineRoles.StateWatch, "watches", properties: Marked("__IsStateWatchPredicate"));
        var notifies = model["notifies"] = await _engine.PlayingAsync(PipelineRoles.NotifiedConnection, "notifies",
            properties: Marked("__IsNotifiedConnectionPredicate"));
        await _engine.PlayingAsync(PipelineRoles.CallRecord, "handledBy");
        await _engine.PlayingAsync(PipelineRoles.AwaitedThing, "awaits");
        await _engine.PlayingAsync(PipelineRoles.RunSubject, "about");
        await _engine.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        var nodeRun = await _engine.PlayingAsync(PipelineRoles.NodeRun, "NodeRun", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);
        model["arch-pipeline"] = await _engine.PlayingAsync(PipelineRoles.Pipeline, "Pipeline", isArchetype: true);
        var nodeKind = model["arch-node"] = await _engine.PlayingAsync(PipelineRoles.PipelineNode, "PipelineNode", isArchetype: true);
        model["arch-input"] = await _engine.PlayingAsync(PipelineRoles.PipelineInput, "PipelineInput", isArchetype: true);
        model["arch-output"] = await _engine.PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput", isArchetype: true);
        model["arch-wait"] = await _engine.PlayingAsync(PipelineRoles.PipelineWait, "PipelineWait", isArchetype: true);
        model["arch-relate"] = await _engine.PlayingAsync(PipelineRoles.PipelineRelate, "PipelineRelate", isArchetype: true);
        var portKind = model["arch-port"] = await _engine.PlayingAsync(PipelineRoles.Port, "Port", isArchetype: true);
        model["arch-wire"] = await _engine.PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire", isArchetype: true);
        model["input-connector"] = await _engine.PlayingAsync(PipelineRoles.InputConnector, "inputConnector");
        model["output-connector"] = await _engine.PlayingAsync(PipelineRoles.OutputConnector, "outputConnector");
        model["taken-on"] = await _engine.PlayingAsync(PipelineRoles.TakenOn, "takenOn");
        model["success"] = await _engine.PlayingAsync(PipelineRoles.Success, "success");
        model["fail"] = await _engine.PlayingAsync(PipelineRoles.Fail, "fail");
        await _engine.PlayingAsync(PipelineRoles.ErrorPort, "error", properties: PortNamed("error", ModelNames.DirectionOut, "string"));
        await _engine.PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.ComparesWith, "comparesWith");
        model["stands-for"] = await _engine.PlayingAsync(PipelineRoles.StandsFor, "standsFor");
        var provenBy = model["proven-by"] = await _engine.PlayingAsync(PipelineRoles.CompletionRange, "provenBy");
        model["awaits-through"] = await _engine.PlayingAsync(PipelineRoles.AwaitsThrough, "awaitsThrough");
        model["reached-along"] = await _engine.PlayingAsync(PipelineRoles.ReachedAlong, "reachedAlong");
        model["carries"] = await _engine.DeclareOnceAsync("carries");

        async Task<Guid> KindPortAsync(string id, string kind, string name, string direction)
        {
            var port = model[id] = await _engine.DeclareAsync(id, properties: PortNamed(name, direction, "any"));
            await _engine.RelateAsync(port, @is, portKind);
            await _engine.RelateAsync(model[kind], has, port);
            return port;
        }
        foreach (var kind in new[] { "arch-input", "arch-output", "arch-wait", "arch-relate" })
            await _engine.RelateAsync(model[kind], @is, nodeKind);
        await KindPortAsync("wait.thing.in", "arch-wait", ModelNames.Thing, ModelNames.DirectionIn);
        await KindPortAsync("wait.thing.out", "arch-wait", ModelNames.Thing, ModelNames.DirectionOut);
        await KindPortAsync("wait.reached.out", "arch-wait", "reached", ModelNames.DirectionOut);
        await KindPortAsync("relate.thing.in", "arch-relate", ModelNames.Thing, ModelNames.DirectionIn);
        await KindPortAsync("relate.thing.out", "arch-relate", ModelNames.Thing, ModelNames.DirectionOut);
        await KindPortAsync("relate.written.out", "arch-relate", ModelNames.Written, ModelNames.DirectionOut);

        var studyKind = model["study-kind"] = await _engine.DeclareAsync("Study", isArchetype: true);
        foreach (var (id, name, writtenAt) in new[] { ("recorded", Recorded, RecordedAt), ("worked-out", WorkedOut, WorkedOutAt) })
        {
            var state = model[id] = await _engine.DeclareAsync(name, properties: new Dictionary<string, object?> { ["Criteria"] = $"{writtenAt} IS KNOWN" });
            await _engine.RelateAsync(state, @is, range);
            await _engine.RelateAsync(state, judges, studyKind);
        }

        var orchestrator = await _engine.DeclareAsync("orchestrator service", properties: new Dictionary<string, object?>
        {
            ["ExecutablePath"] = "the stand-in, already running",
            ["ServicePort"] = _orchestrator.Port.ToString(),
        });
        await _engine.RelateAsync(orchestrator, @is, serviceKind);
        var resumesRun = await _engine.DeclareAsync("resumesRun");
        await _engine.RelateAsync(resumesRun, @is, connectionKind);
        await _engine.RelateAsync(resumesRun, has, orchestrator);
        await _engine.RelateAsync(nodeRun, notifies, resumesRun);

        var service = model["works-out-service"] = await _engine.DeclareAsync("works out service", properties: new Dictionary<string, object?>
        {
            ["ExecutablePath"] = "the stand-in, already running",
            ["ServicePort"] = _service.Port.ToString(),
        });
        await _engine.RelateAsync(service, @is, serviceKind);
        var connection = model["works-out-connection"] = await _engine.DeclareAsync("worksOut connection");
        await _engine.RelateAsync(connection, @is, connectionKind);
        await _engine.RelateAsync(connection, has, service);
        await _engine.RelateAsync(connection, provenBy, model["worked-out"]);
        var predicate = model["works-out"] = await _engine.DeclareAsync("worksOut");
        await _engine.RelateAsync(predicate, notifies, connection);
        return model;
    }

    private async Task<Guid> AStudyAsync(IReadOnlyDictionary<string, Guid> model)
    {
        var study = await _engine.DeclareAsync($"Study {Guid.NewGuid():N}");
        await _engine.RelateAsync(study, model["is"], model["study-kind"]);
        foreach (var (name, writtenAt) in new[] { (Recorded, RecordedAt), (WorkedOut, WorkedOutAt) })
            (await _engine.Admin.PostAsJsonAsync($"/api/things/{study}/ranges", new { Name = name, Criteria = $"{writtenAt} IS KNOWN" }))
                .EnsureSuccessStatusCode();
        return study;
    }

    private async Task WriteNowAsync(Guid study, string property) =>
        (await _engine.Admin.PostAsJsonAsync($"/api/things/{study}/properties",
            new { Name = property, Type = "vos.DateTime", Value = DateTimeOffset.UtcNow })).EnsureSuccessStatusCode();

    private async Task AnsweredAsync(Guid runId) =>
        await Settle.UntilAsync(async () => await WaitOfAsync(runId) is { Answer: WaitAnswer.Done }, "the platform answers the waiting box");

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));

    private async Task<HeldWait?> WaitOfAsync(Guid runId) =>
        (await Gateway().WaitingNodeRunsAsync(CancellationToken.None)).SingleOrDefault(held => held.RunId == runId);

}
