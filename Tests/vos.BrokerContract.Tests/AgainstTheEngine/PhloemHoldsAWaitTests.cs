using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Phloem's gateway holding a wait in the real engine: the watch it writes for a waiting node run, the platform's answer
// to it, and the reads a resumed or restarted orchestrator makes. Its unit tests answer every snapshot by matching text
// in the request, so only these show that the engine takes the writes and answers the shapes it asks for.
public class PhloemHoldsAWaitTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemHoldsAWaitTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway(HttpMessageHandler? transport = null) => new(
        transport is null ? _engine.ClientFactory : new PerCallHttpClientFactory(transport),
        NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    private sealed record Village(Guid PipelineId, Guid Plot, Guid Decided);

    private const string Moisture = "moisture";

    // Every case arranges this and the host is shared by the class, so the vocabulary is written once.
    private async Task<Village> AVillageWithAPlotToWaitForAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var isPredicate = await _engine.TheOnlyThingNamedAsync(ModelNames.Is);
        var has = await _engine.TheOnlyThingNamedAsync(ModelNames.Has);

        await BindOnceAsync(PipelineRoles.PipelineRun, await _engine.DeclareOnceAsync("PipelineRun", isArchetype: true));
        var nodeRun = await _engine.DeclareOnceAsync("NodeRun", isArchetype: true);
        await BindOnceAsync(PipelineRoles.NodeRun, nodeRun);
        await BindOnceAsync(PipelineRoles.Orchestrator, await _engine.DeclareOnceAsync("Phloem prototype", isArchetype: true));
        await BindOnceAsync(PipelineRoles.AwaitedThing, await _engine.DeclareOnceAsync("awaits"));
        var notifies = await _engine.DeclareOnceAsync("notifies", properties: Marked("__IsNotifiedConnectionPredicate"));
        await BindOnceAsync(NotifiedConnectionRole, notifies);
        await BindOnceAsync(PipelineRoles.CallRecord, await _engine.DeclareOnceAsync("handledBy"));
        await BindOnceAsync(PipelineRoles.StateWatch, await _engine.DeclareOnceAsync("watches", properties: Marked("__IsStateWatchPredicate")));
        var judges = await _engine.DeclareOnceAsync("judges", properties: Marked("__IsJudgedThingPredicate"));
        await BindOnceAsync(JudgedThingRole, judges);
        var range = await _engine.DeclareOnceAsync("Range", isArchetype: true, Marked("__IsRangeArchetype"));
        await BindOnceAsync(RangeRole, range);
        var plotArchetype = await _engine.DeclareOnceAsync("Plot", isArchetype: true);

        if (await _engine.FindAsync("resumesRun") is null)
        {
            var connectionArchetype = await _engine.DeclareAsync("PlatformServiceConnection", isArchetype: true, Marked("__IsConnectionArchetype"));
            var serviceArchetype = await _engine.DeclareAsync("Service", isArchetype: true, Marked("__IsServiceArchetype"));
            await BindOnceAsync(PipelineRoles.Connection, connectionArchetype);
            await BindOnceAsync(PipelineRoles.Service, serviceArchetype);
            var resumes = await _engine.DeclareAsync("resumesRun");
            var service = await _engine.DeclareAsync("orchestrator service");
            await _engine.RelateAsync(resumes, isPredicate, connectionArchetype);
            await _engine.RelateAsync(service, isPredicate, serviceArchetype);
            await _engine.RelateAsync(resumes, has, service);
            await _engine.RelateAsync(nodeRun, notifies, resumes);

            var decided = await _engine.DeclareAsync("decided", properties: new Dictionary<string, object?> { ["Criteria"] = $"{Moisture} < 20" });
            await _engine.RelateAsync(decided, isPredicate, range);
            await _engine.RelateAsync(decided, judges, plotArchetype);
        }

        var plot = await _engine.DeclareAsync($"Plot {Guid.NewGuid():N}");
        await _engine.RelateAsync(plot, isPredicate, plotArchetype);
        (await _engine.Admin.PostAsJsonAsync($"/api/things/{plot}/properties", new { Name = Moisture, Type = "vos.LongInteger", Value = 50L }))
            .EnsureSuccessStatusCode();
        (await _engine.Admin.PostAsJsonAsync($"/api/things/{plot}/ranges", new { Name = "decided", Criteria = $"{Moisture} < 20" }))
            .EnsureSuccessStatusCode();
        return new Village(await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), plot, await _engine.TheOnlyThingNamedAsync("decided"));
    }

    private async Task EnterTheRangeAsync(Guid plot) =>
        (await _engine.Admin.PutAsJsonAsync($"/api/things/{plot}/properties", new { Name = Moisture, Type = "vos.LongInteger", Value = 10L }))
            .EnsureSuccessStatusCode();

    private static IReadOnlyDictionary<string, object?> Marked(string mark) => new Dictionary<string, object?> { [mark] = true };

    private static readonly Role RangeRole = new("range", new("c081de17-c325-55b7-99ca-16fb835ae545"));
    private static readonly Role JudgedThingRole = new("judgedThing", new("63d59765-a9b7-53b8-8a2b-0de3899add18"));
    private static readonly Role NotifiedConnectionRole = new("notifiedConnection", new("8ac302e3-3de9-51db-8960-65f33396838e"));

    private async Task BindOnceAsync(Role role, Guid played)
    {
        if (await _engine.FindAsync($"{role.Word} role") is not null) return;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> Noticed =
        new Dictionary<string, JsonElement> { ["notice"] = JsonSerializer.SerializeToElement("sent for review") };

    private static async Task<(Guid RunId, Guid NodeId)> AWaitingNodeRunAsync(MyceliumGateway gateway, Village village)
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, village.PipelineId, CancellationToken.None,
            parameters: JsonSerializer.SerializeToElement(new { subject = new { id = village.Plot, name = "Plot 7" } }));
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Waiting, null, CancellationToken.None, outputs: Noticed,
            watch: new Watch(village.Plot, village.Decided));
        return (runId, nodeId);
    }

    private async Task<HeldWait?> WaitOfAsync(Guid runId) =>
        (await Gateway().WaitingNodeRunsAsync(CancellationToken.None)).SingleOrDefault(held => held.RunId == runId);

    private async Task<HeldWait> AnsweredWaitOfAsync(Guid runId)
    {
        HeldWait? wait = null;
        await Settle.UntilAsync(async () => (wait = await WaitOfAsync(runId)) is { Answer: not WaitAnswer.None },
            "the platform answers the waiting node run");
        return wait!;
    }

    [FactAgainstTheEngine]
    public async Task A_run_is_read_back_with_its_pipeline_parameters_and_each_node_runs_status_outputs_and_watch()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);

        record.Should().NotBeNull();
        record!.PipelineId.Should().Be(village.PipelineId);
        record.Parameters.GetProperty("subject").GetProperty("name").GetString().Should().Be("Plot 7");
        record.Nodes[nodeId].Status.Should().Be(RunStatus.Waiting);
        record.Nodes[nodeId].Outputs["notice"].GetString().Should().Be("sent for review");
        record.Nodes[nodeId].Watching.Should().Be(new Watch(village.Plot, village.Decided));
    }

    // A gateway that has read the run back writes the node run's next status onto the same Thing, as one would
    // after a restart, rather than creating it again.
    [FactAgainstTheEngine]
    public async Task A_gateway_that_read_a_run_back_moves_its_waiting_node_run_on()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var (runId, nodeId) = await AWaitingNodeRunAsync(Gateway(), village);
        var restarted = Gateway();

        await restarted.ReadRunRecordAsync(runId, CancellationToken.None);
        await restarted.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Succeeded, null, CancellationToken.None, outputs: Noticed);

        (await restarted.ReadRunRecordAsync(runId, CancellationToken.None))!.Nodes[nodeId].Status.Should().Be(RunStatus.Succeeded);
    }

    [FactAgainstTheEngine]
    public async Task Waiting_is_one_write_and_resuming_is_one_write()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var transport = new RecordingTransport(_engine.Transport);
        var gateway = Gateway(transport);
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, village.PipelineId, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
        var watch = new Watch(village.Plot, village.Decided);

        var beforeWaiting = transport.Writes.Count;
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Waiting, null, CancellationToken.None, outputs: Noticed, watch: watch);
        var waitingWrites = transport.Writes.Count - beforeWaiting;
        (await WaitOfAsync(runId))!.Watching.Should().Be(watch);

        var beforeResuming = transport.Writes.Count;
        await gateway.ResumeFromWaitAsync(runId, nodeId, "Review", Noticed, null, watch, CancellationToken.None);
        var resumingWrites = transport.Writes.Count - beforeResuming;

        waitingWrites.Should().Be(1);
        resumingWrites.Should().Be(1);
        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);
        record!.Status.Should().Be(RunStatus.Running);
        record.Nodes[nodeId].Status.Should().Be(RunStatus.Succeeded);
        record.Nodes[nodeId].Watching.Should().BeNull();
    }

    [FactAgainstTheEngine]
    public async Task The_platform_answers_a_waiting_node_run_when_its_Thing_enters_the_range_and_a_restart_reads_that_answer()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var (runId, nodeId) = await AWaitingNodeRunAsync(Gateway(), village);
        (await WaitOfAsync(runId))!.Answer.Should().Be(WaitAnswer.None);

        await EnterTheRangeAsync(village.Plot);

        var answered = await AnsweredWaitOfAsync(runId);
        answered.Should().Be(new HeldWait(runId, nodeId, new Watch(village.Plot, village.Decided), answered.Answer));
        (await CallRecordsFromAsync(village.Plot)).Should().ContainSingle().Which.TargetId.Should().Be(await NodeRunIdOfAsync(runId),
            "the platform's record targets the node run itself");
    }

    [FactAgainstTheEngine]
    public async Task A_Thing_already_in_the_range_is_answered_as_its_watch_is_written()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        await EnterTheRangeAsync(village.Plot);

        var (runId, _) = await AWaitingNodeRunAsync(Gateway(), village);

        (await AnsweredWaitOfAsync(runId)).Answer.Should().NotBe(WaitAnswer.None);
    }

    [FactAgainstTheEngine]
    public async Task A_wait_ended_by_another_status_retracts_its_watch()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);
        await EnterTheRangeAsync(village.Plot);
        await AnsweredWaitOfAsync(runId);

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Cancelled, "withdrawn", CancellationToken.None,
            watch: new Watch(village.Plot, village.Decided));

        (await WaitOfAsync(runId)).Should().BeNull();
        (await gateway.ReadRunRecordAsync(runId, CancellationToken.None))!.Nodes[nodeId].Watching.Should().BeNull();
    }

    [FactAgainstTheEngine]
    public async Task A_node_run_is_found_with_its_run_and_node_and_the_Thing_it_waits_for_is_not_taken_for_one()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);
        var nodeRun = (await WaitOfAsync(runId))!;

        var found = await gateway.NodeRunAsync(await NodeRunIdOfAsync(runId), CancellationToken.None);

        found.Should().Be(new HeldNodeRun(runId, nodeId));
        nodeRun.NodeId.Should().Be(nodeId);
        (await gateway.NodeRunAsync(village.Plot, CancellationToken.None)).Should().BeNull();
    }

    private async Task<IReadOnlyList<GraphRelationship>> CallRecordsFromAsync(Guid thingId)
    {
        var read = await _engine.Admin.PostAsJsonAsync("/api/snapshots", new
        {
            ids = new[] { thingId, PipelineRoles.CallRecord.Id },
            names = new[] { ModelNames.Binds },
            traverse = new object[] { new { predicate = ModelNames.Binds, direction = "outgoing", depth = 1 } },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[]
            {
                new { predicate = ModelNames.Binds, direction = "outgoing" },
                new { predicateRole = PipelineRoles.CallRecord.Id, direction = "outgoing" },
            },
        });
        read.EnsureSuccessStatusCode();
        var graph = SnapshotParser.Parse(await read.Content.ReadFromJsonAsync<JsonElement>());
        return graph.RelationshipsAlong(graph.Thing(thingId)!, PipelineRoles.CallRecord).ToList();
    }

    private async Task<Guid> NodeRunIdOfAsync(Guid runId)
    {
        var read = await _engine.Admin.PostAsJsonAsync("/api/snapshots", new
        {
            ids = new[] { runId },
            names = new[] { ModelNames.Has },
            includeIsAncestors = false,
            includeRelationships = true,
            relationships = new object[] { new { predicate = ModelNames.Has, direction = "outgoing" } },
        });
        read.EnsureSuccessStatusCode();
        var graph = SnapshotParser.Parse(await read.Content.ReadFromJsonAsync<JsonElement>());
        return graph.RelationshipsFrom(graph.Thing(runId)!, ModelNames.Has).Single().TargetId;
    }

    [FactAgainstTheEngine]
    public async Task A_watch_over_a_Thing_the_model_does_not_hold_is_refused_with_the_platforms_reason()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, village.PipelineId, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
        var gone = Guid.NewGuid();

        var waiting = () => gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Waiting, null, CancellationToken.None,
            watch: new Watch(gone, village.Decided));

        (await waiting.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(gone.ToString());
        (await gateway.ReadRunRecordAsync(runId, CancellationToken.None))!.Nodes[nodeId].Status.Should().Be(RunStatus.Running);
    }

    [FactAgainstTheEngine]
    public async Task A_restarted_orchestrator_reads_the_waiting_node_run_and_none_of_the_many_finished()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var finished = new List<Guid>();
        for (var run = 0; run < 25; run++)
        {
            var runId = Guid.NewGuid();
            var nodeId = Guid.NewGuid();
            await gateway.CreateRunAsync(runId, village.PipelineId, CancellationToken.None);
            await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
            await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Succeeded, null, CancellationToken.None, outputs: Noticed);
            await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);
            finished.Add(runId);
        }
        var (waitingRun, waitingNode) = await AWaitingNodeRunAsync(gateway, village);
        var transport = new RecordingTransport(_engine.Transport);

        var waits = await Gateway(transport).WaitingNodeRunsAsync(CancellationToken.None);

        waits.Should().ContainSingle(wait => wait.RunId == waitingRun).Which.NodeId.Should().Be(waitingNode);
        var read = transport.SnapshotBodies.Should().ContainSingle().Subject;
        read.Should().Contain(waitingRun.ToString());
        read.Should().NotContainAny(finished.Select(runId => runId.ToString()));
    }

    private sealed class RecordingTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<string> SnapshotBodies { get; } = new();
        public List<string> Writes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/snapshots")
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                SnapshotBodies.Add(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            else if (request.Method != HttpMethod.Get)
                lock (Writes) Writes.Add($"{request.Method} {path}");
            return response;
        }
    }
}
