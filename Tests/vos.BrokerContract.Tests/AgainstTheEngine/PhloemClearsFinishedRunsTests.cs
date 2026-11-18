using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemClearsFinishedRunsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemClearsFinishedRunsTests(TheEngine engine) => _engine = engine;

    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private async Task<MyceliumGateway> AGatewayAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var binds = await _engine.TheOnlyThingNamedAsync(ModelNames.Binds);
        var kindPeriods = new Dictionary<string, object?>
        {
            [ModelNames.KeepRunsForDays] = 30, [ModelNames.KeepFailedRunsForDays] = 90, [ModelNames.ClearsAtMostRunsPerLook] = 200,
        };
        await BindOnceAsync(PipelineRoles.PipelineRun, await _engine.DeclareOnceAsync("PipelineRun", isArchetype: true, kindPeriods), binds);
        await BindOnceAsync(PipelineRoles.NodeRun, await _engine.DeclareOnceAsync("NodeRun", isArchetype: true), binds);
        await BindOnceAsync(PipelineRoles.Attempt, await _engine.DeclareOnceAsync("Attempt", isArchetype: true), binds);
        await BindOnceAsync(PipelineRoles.Orchestrator, await _engine.DeclareOnceAsync("Phloem prototype", isArchetype: true), binds);
        return new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));
    }

    private async Task<Guid> ARunOfAsync(MyceliumGateway gateway, Guid pipelineId, string? endsAs)
    {
        var runId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);
        if (endsAs is not null) await gateway.SetRunStatusAsync(runId, endsAs, CancellationToken.None);
        return runId;
    }

    [FactAgainstTheEngine]
    public async Task Each_run_is_judged_with_how_it_ended_its_pipeline_and_whether_the_orchestrator_holds_it()
    {
        var gateway = await AGatewayAsync();
        var pipelineId = await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}",
            properties: new Dictionary<string, object?> { [ModelNames.KeepRunsForDays] = 7 });
        var failed = await ARunOfAsync(gateway, pipelineId, RunStatus.Failed);
        var running = await ARunOfAsync(gateway, pipelineId, endsAs: null);

        var judged = await gateway.RunsToJudgeAsync(CancellationToken.None);

        judged.Kind.Should().Be(new KeepPeriods(30, 90));
        judged.RunsPerLook.Should().Be(200);
        judged.Pipelines[pipelineId].Should().Be(new KeepPeriods(7, null));
        var failedRun = judged.Runs.Single(run => run.RunId == failed);
        failedRun.Should().BeEquivalentTo(new { PipelineId = (Guid?)pipelineId, Status = RunStatus.Failed, Held = false });
        failedRun.FinishedAt.Should().NotBeNull();
        judged.Runs.Single(run => run.RunId == running).Should().BeEquivalentTo(new { Status = RunStatus.Running, Held = true, FinishedAt = (DateTimeOffset?)null });
    }

    // Eight boxes, one of them over three items, each with one attempt, is about the size of a discovery run.
    [FactAgainstTheEngine]
    public async Task Clearing_a_run_retracts_it_with_every_node_run_and_attempt_and_counts_them()
    {
        var gateway = await AGatewayAsync();
        var runId = await ARunOfAsync(gateway, await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), endsAs: null);
        var nodeRuns = new List<(Guid Node, int? Index)>();
        for (var box = 0; box < 7; box++) nodeRuns.Add((Guid.NewGuid(), null));
        var forEach = Guid.NewGuid();
        for (var item = 0; item < 3; item++) nodeRuns.Add((forEach, item));
        foreach (var (node, index) in nodeRuns)
        {
            await gateway.SetNodeRunStatusAsync(runId, node, "Box", RunStatus.Succeeded, null, CancellationToken.None, index, index is null ? 0 : 3);
            await gateway.RecordAttemptAsync(runId, node, index, new AttemptRecord(1, Noon, Noon.AddSeconds(1), RunStatus.Succeeded, null), CancellationToken.None);
        }
        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);
        var before = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);

        var cleared = await gateway.ClearRunAsync(runId, CancellationToken.None);

        cleared.Should().Be(1 + 2 * nodeRuns.Count, "the run, each node run and each attempt");
        before.Should().NotBeNull();
        (await gateway.ReadRunRecordAsync(runId, CancellationToken.None)).Should().BeNull();
        (await gateway.RunsToJudgeAsync(CancellationToken.None)).Runs.Should().NotContain(run => run.RunId == runId);
        (await gateway.ClearRunAsync(runId, CancellationToken.None)).Should().Be(0, "a run already gone has nothing left to clear");
    }

    [FactAgainstTheEngine]
    public async Task A_stop_request_is_cleared_once_with_the_last_run_it_names_and_not_before()
    {
        var gateway = await AGatewayAsync();
        var binds = await _engine.TheOnlyThingNamedAsync(ModelNames.Binds);
        var requestedBy = await _engine.DeclareOnceAsync("requestedBy");
        var stoppedBy = await _engine.DeclareOnceAsync("stoppedBy");
        await BindOnceAsync(PipelineRoles.RequestedBy, requestedBy, binds);
        await BindOnceAsync(PipelineRoles.StoppedBy, stoppedBy, binds);
        var pipelineId = await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
        var requester = await ARunOfAsync(gateway, pipelineId, RunStatus.Succeeded);
        var first = await ARunOfAsync(gateway, pipelineId, RunStatus.Cancelled);
        var last = await ARunOfAsync(gateway, pipelineId, RunStatus.Cancelled);
        var requestName = $"StopRequest {Guid.NewGuid():N}";
        var request = await _engine.DeclareAsync(requestName);
        await _engine.RelateAsync(request, requestedBy, requester);
        await _engine.RelateAsync(first, stoppedBy, request);
        await _engine.RelateAsync(last, stoppedBy, request);

        var clearedFirst = await gateway.ClearRunAsync(first, CancellationToken.None);
        var clearedRequester = await gateway.ClearRunAsync(requester, CancellationToken.None);
        var keptAfterTwo = await _engine.FindAsync(requestName);
        var clearedLast = await gateway.ClearRunAsync(last, CancellationToken.None);

        (clearedFirst, clearedRequester).Should().Be((1, 1));
        keptAfterTwo.Should().Be(request, "the last run it stopped is still kept");
        clearedLast.Should().Be(2, "the last run and the stop request with it");
        (await _engine.FindAsync(requestName)).Should().BeNull();
    }

    private async Task BindOnceAsync(Role role, Guid played, Guid binds)
    {
        if (await _engine.FindAsync($"{role.Word} role") is not null) return;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, binds, played);
    }
}
