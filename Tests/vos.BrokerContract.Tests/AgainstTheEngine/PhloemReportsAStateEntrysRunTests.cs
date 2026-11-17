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

public sealed class PhloemReportsAStateEntrysRunTests : IClassFixture<TheEngine>, IAsyncLifetime
{
    private readonly TheEngine _engine;
    private readonly OrchestratorStandIn _orchestrator = new();

    public PhloemReportsAStateEntrysRunTests(TheEngine engine) => _engine = engine;

    public Task InitializeAsync() => _orchestrator.StartAsync();

    public Task DisposeAsync() => _orchestrator.DisposeAsync().AsTask();

    [FactAgainstTheEngine]
    public async Task A_run_a_state_entry_started_leaves_its_call_record_Received_while_it_goes_and_Completed_when_it_ends()
    {
        var model = await StateEntryModel.ArrangeAsync(_engine, _orchestrator.Port, drawsAWholePipeline: true);
        var transport = new HoldingTheEndReport(_engine.Transport);

        var call = await model.EnterOutOfDateAsync(_orchestrator);
        var running = model.StartTheRunAsync(call, transport);
        await Settle.UntilAsync(async () => await model.CallStateAsync(call.CallRecordId) == "Received", "the run's start reaches the record");
        var whileTheRunGoes = await model.CallStateAsync(call.CallRecordId);
        transport.LetTheEndGo();
        var result = await running;
        await Settle.UntilAsync(async () => await model.CallStateAsync(call.CallRecordId) == "Completed", "the run's end reaches the record");

        result.Success.Should().BeTrue(result.Error);
        whileTheRunGoes.Should().Be("Received");
        (await model.Gateway().ReadRunRecordAsync(result.RunId, CancellationToken.None))!.CallRecordId.Should().Be(call.CallRecordId);
        await Settle.UntilAsync(async () => (await model.Gateway().OwedReportsAsync(CancellationToken.None)).All(owed => owed.RunId != result.RunId),
            "a run whose last report was taken is held no longer");
    }

    [FactAgainstTheEngine]
    public async Task A_run_a_state_entry_started_that_fails_leaves_its_call_record_Failed_with_the_reason()
    {
        var model = await StateEntryModel.ArrangeAsync(_engine, _orchestrator.Port, drawsAWholePipeline: false);

        var call = await model.EnterOutOfDateAsync(_orchestrator);
        var result = await model.StartTheRunAsync(call, _engine.Transport);
        await Settle.UntilAsync(async () => await model.CallStateAsync(call.CallRecordId) == "Failed", "the run's failure reaches the record");

        result.Success.Should().BeFalse();
        (await model.CallErrorAsync(call.CallRecordId)).Should().Be(result.Error).And.NotBeNullOrWhiteSpace();
    }

    private sealed record StateEntryCall(SpawnTrigger Trigger, Guid CallRecordId);

    private sealed class StateEntryModel
    {
        private readonly TheEngine _engine;
        private readonly Guid _catchmentKind;

        private StateEntryModel(TheEngine engine, Guid catchmentKind)
        {
            _engine = engine;
            _catchmentKind = catchmentKind;
        }

        private const string RainfallAge = "rainfallAge";
        private const string OutOfDate = "OutOfDate";
        private const string Criteria = $"{RainfallAge} > 30";

        private static readonly Role RangeRole = new("range", RoleIdentity.Of("range"));
        private static readonly Role JudgedThingRole = new("judgedThing", RoleIdentity.Of("judgedThing"));
        private static readonly Role TriggerRole = new("trigger", RoleIdentity.Of("trigger"));
        private static readonly Role StateTriggerRole = new("stateTrigger", RoleIdentity.Of("stateTrigger"));
        private static readonly Role CompletesWhenRole = new("completesWhen", RoleIdentity.Of("completesWhen"));
        private static readonly Role WhenTheServiceReportsCompletionRole =
            new("whenTheServiceReportsCompletion", RoleIdentity.Of("whenTheServiceReportsCompletion"));

        public MyceliumGateway Gateway(HttpMessageHandler? transport = null) => new(
            new PerCallHttpClientFactory(transport ?? _engine.Transport), NullLogger<MyceliumGateway>.Instance, TheEngine.Url,
            new ModelClock(), new LaunchToken(_engine.AdminToken));

        // The host is shared by the class, so the vocabulary is written once and each case arranges its own Catchment kind.
        public static async Task<StateEntryModel> ArrangeAsync(TheEngine engine, int orchestratorPort, bool drawsAWholePipeline)
        {
            foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
                await engine.DeclareOnceAsync(predicate);
            var vocabulary = new Vocabulary(engine);
            var firstCase = await engine.FindAsync($"{PipelineRoles.PipelineInput.Word} role") is null;
            var isPredicate = await engine.TheOnlyThingNamedAsync(ModelNames.Is);
            var has = await engine.TheOnlyThingNamedAsync(ModelNames.Has);

            var connectionKind = await vocabulary.PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection", isArchetype: true,
                Marked("__IsConnectionArchetype"));
            var serviceKind = await vocabulary.PlayingAsync(PipelineRoles.Service, "Service", isArchetype: true, Marked("__IsServiceArchetype"));
            var range = await vocabulary.PlayingAsync(RangeRole, "Range", isArchetype: true, Marked("__IsRangeArchetype"));
            var judges = await vocabulary.PlayingAsync(JudgedThingRole, "judges", properties: Marked("__IsJudgedThingPredicate"));
            var watches = await vocabulary.PlayingAsync(PipelineRoles.StateWatch, "watches", properties: Marked("__IsStateWatchPredicate"));
            var triggeredBy = await vocabulary.PlayingAsync(TriggerRole, "triggeredBy");
            var stateTrigger = await vocabulary.PlayingAsync(StateTriggerRole, "state");
            var completesWhen = await vocabulary.PlayingAsync(CompletesWhenRole, "completesWhen");
            var onTheReport = await vocabulary.PlayingAsync(WhenTheServiceReportsCompletionRole, "whenTheServiceReportsCompletion");
            await vocabulary.PlayingAsync(PipelineRoles.CallRecord, "handledBy");
            await vocabulary.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
            await vocabulary.PlayingAsync(PipelineRoles.NodeRun, "NodeRun", isArchetype: true);
            await vocabulary.PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);
            await vocabulary.PlayingAsync(PipelineRoles.RunSubject, "about");
            var pipelineKind = await vocabulary.PlayingAsync(PipelineRoles.Pipeline, "Pipeline", isArchetype: true);
            var node = await vocabulary.PlayingAsync(PipelineRoles.PipelineNode, "PipelineNode", isArchetype: true);
            var input = await vocabulary.PlayingAsync(PipelineRoles.PipelineInput, "PipelineInput", isArchetype: true);
            var output = await vocabulary.PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput", isArchetype: true);
            var port = await vocabulary.PlayingAsync(PipelineRoles.Port, "Port", isArchetype: true);
            var wireKind = await vocabulary.PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire", isArchetype: true);
            var inputConnector = await vocabulary.PlayingAsync(PipelineRoles.InputConnector, "inputConnector");
            var outputConnector = await vocabulary.PlayingAsync(PipelineRoles.OutputConnector, "outputConnector");
            var takenOn = await vocabulary.PlayingAsync(PipelineRoles.TakenOn, "takenOn");
            var success = await vocabulary.PlayingAsync(PipelineRoles.Success, "success");
            await vocabulary.PlayingAsync(PipelineRoles.Fail, "fail");
            await vocabulary.PlayingAsync(PipelineRoles.ErrorPort, "error", properties: new Dictionary<string, object?>
            {
                [ModelNames.PortName] = "error", [ModelNames.Direction] = ModelNames.DirectionOut, [ModelNames.PortType] = "string",
            });
            await vocabulary.PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath", isArchetype: true);
            await vocabulary.PlayingAsync(PipelineRoles.ComparesWith, "comparesWith");
            var standsFor = await vocabulary.PlayingAsync(PipelineRoles.StandsFor, "standsFor");
            var carries = await engine.DeclareOnceAsync("carries");
            if (firstCase)
            {
                await engine.RelateAsync(input, isPredicate, node);
                await engine.RelateAsync(output, isPredicate, node);
            }

            var catchmentKind = await engine.DeclareAsync($"Catchment {Guid.NewGuid():N}", isArchetype: true);
            var outOfDate = await engine.DeclareAsync(OutOfDate, properties: new Dictionary<string, object?> { ["Criteria"] = Criteria });
            await engine.RelateAsync(outOfDate, isPredicate, range);
            await engine.RelateAsync(outOfDate, judges, catchmentKind);

            var service = await engine.DeclareAsync($"orchestrator service {Guid.NewGuid():N}", properties: new Dictionary<string, object?>
            {
                ["ExecutablePath"] = "the orchestrator, already running",
                ["ServicePort"] = orchestratorPort.ToString(),
            });
            await engine.RelateAsync(service, isPredicate, serviceKind);
            var estimateRainfall = await engine.DeclareAsync("Estimate-Rainfall");
            await engine.RelateAsync(estimateRainfall, isPredicate, connectionKind);
            await engine.RelateAsync(estimateRainfall, has, service);
            await engine.RelateAsync(estimateRainfall, triggeredBy, stateTrigger);
            await engine.RelateAsync(estimateRainfall, watches, outOfDate);
            await engine.RelateAsync(estimateRainfall, completesWhen, onTheReport);

            var pipeline = await engine.DeclareAsync($"Estimate rainfall {Guid.NewGuid():N}",
                properties: new Dictionary<string, object?> { [ModelNames.Published] = true });
            await engine.RelateAsync(pipeline, isPredicate, pipelineKind);
            var arrival = await engine.DeclareAsync("Arrival");
            await engine.RelateAsync(arrival, isPredicate, input);
            await engine.RelateAsync(arrival, standsFor, estimateRainfall);
            await engine.RelateAsync(pipeline, has, arrival);
            var subjectPort = await engine.DeclareAsync("arrival.subject", properties: new Dictionary<string, object?>
            {
                [ModelNames.Direction] = ModelNames.DirectionOut, [ModelNames.PortType] = "any", [ModelNames.PortName] = PipelineExecutor.SubjectParam,
            });
            await engine.RelateAsync(subjectPort, isPredicate, port);
            await engine.RelateAsync(arrival, has, subjectPort);

            // A pipeline with no end node cannot be built, so its run is refused at its start and fails with that reason.
            if (drawsAWholePipeline)
            {
                var end = await engine.DeclareAsync("Rainfall estimated");
                await engine.RelateAsync(end, isPredicate, output);
                await engine.RelateAsync(pipeline, has, end);
                var resultPort = await engine.DeclareAsync("end.result", properties: new Dictionary<string, object?>
                {
                    [ModelNames.Direction] = ModelNames.DirectionIn, [ModelNames.PortType] = "any", [ModelNames.PortName] = "result",
                });
                await engine.RelateAsync(resultPort, isPredicate, port);
                await engine.RelateAsync(end, has, resultPort);
                var wire = await engine.DeclareAsync("arrival.subject to end.result");
                await engine.RelateAsync(wire, isPredicate, wireKind);
                await engine.RelateAsync(arrival, has, wire);
                await engine.RelateAsync(wire, carries, end);
                await engine.RelateAsync(wire, inputConnector, subjectPort);
                await engine.RelateAsync(wire, outputConnector, resultPort);
                await engine.RelateAsync(wire, takenOn, success);
            }

            return new StateEntryModel(engine, catchmentKind);
        }

        public async Task<StateEntryCall> EnterOutOfDateAsync(OrchestratorStandIn orchestrator)
        {
            var catchment = await _engine.DeclareAsync($"Catchment 3 {Guid.NewGuid():N}");
            await _engine.RelateAsync(catchment, await _engine.TheOnlyThingNamedAsync(ModelNames.Is), _catchmentKind);
            (await _engine.Admin.PostAsJsonAsync($"/api/things/{catchment}/properties", new { Name = RainfallAge, Type = "vos.LongInteger", Value = 0L }))
                .EnsureSuccessStatusCode();
            (await _engine.Admin.PostAsJsonAsync($"/api/things/{catchment}/ranges", new { Name = OutOfDate, Criteria }))
                .EnsureSuccessStatusCode();

            (await _engine.Admin.PutAsJsonAsync($"/api/things/{catchment}/properties", new { Name = RainfallAge, Type = "vos.LongInteger", Value = 40L }))
                .EnsureSuccessStatusCode();

            JsonElement body = default;
            await Settle.UntilAsync(() => orchestrator.CallAbout(catchment) is { } sent && (body = sent).ValueKind == JsonValueKind.Object,
                "the engine calls the orchestrator for the Catchment's entry into OutOfDate");
            var trigger = SpawnTrigger.Resolve(body);
            return new StateEntryCall(trigger, trigger.RelationshipId!.Value);
        }

        // The orchestrator's /handle cannot be hosted beside the engine, whose entry point has the same name, so its steps are taken here.
        public async Task<PipelineRunResult> StartTheRunAsync(StateEntryCall call, HttpMessageHandler transport)
        {
            var gateway = Gateway(transport);
            var start = await new PipelineStarter(gateway).ResolveAsync(call.Trigger.TargetId, call.Trigger.RelationshipId, call.Trigger.Subject?.Id,
                CancellationToken.None);
            start.Refusal.Should().BeNull();
            start.CallRecordId.Should().Be(call.CallRecordId, "a state entry's relationship is its call record");
            var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
            return await executor.RunAsync(start.PipelineId!.Value, call.Trigger.Parameters, CancellationToken.None, subject: call.Trigger.Subject,
                callRecordId: start.CallRecordId);
        }

        public async Task<string?> CallStateAsync(Guid callRecordId) => await PropertyOfAsync(callRecordId, "__CallState");

        public async Task<string?> CallErrorAsync(Guid callRecordId) => await PropertyOfAsync(callRecordId, "__CallLastError");

        private async Task<string?> PropertyOfAsync(Guid relationshipId, string property)
        {
            var read = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/relationships/{relationshipId}");
            if (!MyceliumClientBase.TryGetPropertyCaseInsensitive(read, "Properties", out var properties)
                || !MyceliumClientBase.TryGetPropertyCaseInsensitive(properties, property, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Object && MyceliumClientBase.TryGetPropertyCaseInsensitive(value, "value", out var inner))
                value = inner;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static IReadOnlyDictionary<string, object?> Marked(string mark) => new Dictionary<string, object?> { [mark] = true };
    }

    private sealed class Vocabulary(TheEngine engine)
    {
        public async Task<Guid> PlayingAsync(Role role, string name, bool isArchetype = false, IReadOnlyDictionary<string, object?>? properties = null)
        {
            if (await engine.FindAsync($"{role.Word} role") is not null)
                return await engine.TheOnlyThingNamedAsync($"{name} · {role.Word}");
            var played = await engine.DeclareAsync($"{name} · {role.Word}", isArchetype, properties);
            await engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
            await engine.RelateAsync(role.Id, await engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
            return played;
        }
    }

    // Keeps each body the engine sends, so a case starts its run from what was actually sent.
    private sealed class OrchestratorStandIn : IAsyncDisposable
    {
        private readonly List<JsonElement> _calls = new();
        private WebApplication? _app;

        public int Port { get; } = FreeLoopbackPort();

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.Urls.Add($"http://localhost:{Port}");
            _app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
            _app.MapPost("/handle", async (HttpRequest request) =>
            {
                var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
                lock (_calls) _calls.Add(body);
                return Results.Ok(new { success = true, accepted = true });
            });
            await _app.StartAsync();
        }

        public JsonElement? CallAbout(Guid subjectId)
        {
            lock (_calls)
                return _calls.Where(call => call.TryGetProperty("subjectId", out var subject) && subject.GetString() == subjectId.ToString())
                    .Select(call => (JsonElement?)call).FirstOrDefault();
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

    // Holds the run's completed report until the case has read the record while the run is still going.
    private sealed class HoldingTheEndReport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly TaskCompletionSource _endMayGo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void LetTheEndGo() => _endMayGo.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/report", StringComparison.Ordinal)
                && (await request.Content!.ReadAsStringAsync(cancellationToken)).Contains("\"completed\"", StringComparison.Ordinal))
                await _endMayGo.Task;
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
