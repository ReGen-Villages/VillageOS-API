using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The kinds declare every name the gateway writes as the shared site template does, CurrentOnly with no value, so each
// write lands on the Thing's own copy of a declaration rather than on a property of its own.
public class PhloemReadsBackARunWhoseKindsKeepNoHistoryTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsBackARunWhoseKindsKeepNoHistoryTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));

    private static readonly (Role Role, string Kind, string[] Names)[] Kinds =
    [
        (PipelineRoles.PipelineRun, "RunKind",
            [ModelNames.Status, "pipelineId", ModelNames.StartedUtc, ModelNames.FinishedUtc, ModelNames.Parameters, ModelNames.Result,
                ModelNames.Error, ModelNames.Rehearsal, ModelNames.RequestId, ModelNames.CallRecordId]),
        (PipelineRoles.NodeRun, "NodeRunKind",
            [ModelNames.Status, ModelNames.NodeId, ModelNames.Error, ModelNames.StartedUtc, ModelNames.FinishedUtc, ModelNames.Outputs,
                ModelNames.Attempts, ModelNames.Decision, ModelNames.WaitsUntil, ModelNames.Index, ModelNames.Total]),
        (PipelineRoles.Attempt, "AttemptKind",
            [ModelNames.Number, ModelNames.StartedUtc, ModelNames.FinishedUtc, ModelNames.Status, ModelNames.Error]),
    ];

    private async Task<Guid> AModelWhoseRunKindsKeepNoHistoryAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);

        foreach (var (role, kind, names) in Kinds)
        {
            if (await _engine.FindAsync(kind) is not null) continue;
            var kindId = Guid.NewGuid();
            (await _engine.Admin.PostAsJsonAsync("/api/model/fragment", new
            {
                name = kind,
                things = new[]
                {
                    new
                    {
                        id = kindId, name = kind, isArchetype = true,
                        properties = names.ToDictionary(name => name, _ => (object)new { typeInfo = "vos.String", mode = "CurrentOnly" }),
                    },
                },
            })).EnsureSuccessStatusCode();
            await _engine.BindAsync(role, kindId);
        }
        await _engine.PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);

        return await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
    }

    private static JsonElement OwnCopyOf(JsonElement thing, string kind, string name) =>
        thing.GetProperty("InheritedOverrides").EnumerateObject()
            .Single(set => set.Value.GetProperty("SourceName").GetString() == kind)
            .Value.GetProperty("Properties").GetProperty(name);

    [FactAgainstTheEngine]
    public async Task A_finished_run_is_read_back_with_its_node_attempt_and_result_and_keeps_one_status()
    {
        var pipelineId = await AModelWhoseRunKindsKeepNoHistoryAsync();
        var gateway = Gateway();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var outputs = new Dictionary<string, JsonElement> { ["echo"] = JsonSerializer.SerializeToElement("hello") };

        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None, parameters: JsonSerializer.SerializeToElement(new { site = 7 }));
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.RecordAttemptAsync(runId, nodeId, null,
            new AttemptRecord(1, started, started.AddSeconds(1), RunStatus.Succeeded, null), CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Succeeded, null, CancellationToken.None, outputs: outputs);
        await gateway.SetRunResultAsync(runId, JsonSerializer.SerializeToElement(new { echo = "hello" }), CancellationToken.None);
        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);
        record.Should().NotBeNull();
        record!.Status.Should().Be(RunStatus.Succeeded);
        record.Parameters.GetProperty("site").GetInt32().Should().Be(7);
        var node = record.Nodes[nodeId];
        node.Status.Should().Be(RunStatus.Succeeded);
        node.Outputs["echo"].GetString().Should().Be("hello");
        node.AttemptsMade.Should().ContainSingle().Which.Number.Should().Be(1);

        var run = await _engine.ReadAsync(runId);
        OwnCopyOf(run, "RunKind", ModelNames.Result).GetProperty("value").GetString().Should().Be("{\"echo\":\"hello\"}");
        var statuses = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{runId}/properties/{ModelNames.Status}/history");
        statuses.GetProperty("samples").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("value").GetString().Should().Be(RunStatus.Succeeded);
    }

    [FactAgainstTheEngine]
    public async Task A_node_run_counts_its_attempts_on_its_own_copy()
    {
        var pipelineId = await AModelWhoseRunKindsKeepNoHistoryAsync();
        var gateway = Gateway();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var nodeName = $"Echo {nodeId:N}";
        var started = DateTimeOffset.UtcNow;
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Running, null, CancellationToken.None);

        await gateway.RecordAttemptAsync(runId, nodeId, null,
            new AttemptRecord(1, started, started.AddSeconds(1), RunStatus.Failed, "the service was away"), CancellationToken.None);
        await gateway.RecordAttemptAsync(runId, nodeId, null,
            new AttemptRecord(2, started.AddSeconds(2), started.AddSeconds(3), RunStatus.Succeeded, null), CancellationToken.None);

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);
        record!.Nodes[nodeId].AttemptsMade.Select(attempt => (attempt.Number, attempt.Status, attempt.Error))
            .Should().Equal((1, RunStatus.Failed, "the service was away"), (2, RunStatus.Succeeded, null));
        var nodeRun = await _engine.ReadAsync(await _engine.TheOnlyThingNamedAsync($"NodeRun {nodeName}"));
        OwnCopyOf(nodeRun, "NodeRunKind", ModelNames.Attempts).GetProperty("value").GetString().Should().Be("2");
    }

    [FactAgainstTheEngine]
    public async Task A_waiting_node_run_is_offered_to_a_restarted_orchestrator_with_the_instant_its_wait_fails_at()
    {
        var pipelineId = await AModelWhoseRunKindsKeepNoHistoryAsync();
        var gateway = Gateway();
        var runId = Guid.NewGuid();
        var waiting = Guid.NewGuid();
        var running = Guid.NewGuid();
        var until = new DateTimeOffset(2031, 5, 4, 12, 0, 0, TimeSpan.Zero);
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, waiting, "Review", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, waiting, "Review", RunStatus.Waiting, null, CancellationToken.None,
            watch: new Watch(null, null, until));
        await gateway.SetNodeRunStatusAsync(runId, running, "Echo", RunStatus.Running, null, CancellationToken.None);

        var waits = (await Gateway().WaitingNodeRunsAsync(CancellationToken.None)).Where(wait => wait.RunId == runId).ToList();

        waits.Should().ContainSingle().Which.NodeId.Should().Be(waiting);
        waits[0].Watching.Until.Should().Be(until);
    }
}
