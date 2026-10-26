using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The attempts a node run makes, as the engine holds them: written one by one, read back in order with the run's
// record, and the node run found again while it stands between attempts.
public class PhloemRecordsAttemptsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsAttemptsTests(TheEngine engine) => _engine = engine;

    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private async Task<(MyceliumGateway Gateway, Guid RunId, Guid NodeId)> ANodeRunThatTriedAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var binds = await _engine.TheOnlyThingNamedAsync(ModelNames.Binds);
        await BindOnceAsync(PipelineRoles.PipelineRun, await _engine.DeclareOnceAsync("PipelineRun", isArchetype: true), binds);
        await BindOnceAsync(PipelineRoles.NodeRun, await _engine.DeclareOnceAsync("NodeRun", isArchetype: true), binds);
        await BindOnceAsync(PipelineRoles.Attempt, await _engine.DeclareOnceAsync("Attempt", isArchetype: true), binds);
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.RecordAttemptAsync(runId, nodeId, null, new AttemptRecord(1, Noon, Noon.AddSeconds(1), RunStatus.Failed, "the service was away"), CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Trying, "the service was away", CancellationToken.None);
        await gateway.RecordAttemptAsync(runId, nodeId, null, new AttemptRecord(2, Noon.AddMinutes(1), Noon.AddMinutes(1).AddSeconds(1), RunStatus.Failed, "still away"), CancellationToken.None);
        return (gateway, runId, nodeId);
    }

    [FactAgainstTheEngine]
    public async Task Each_attempt_is_read_back_in_order_with_its_instants_and_the_node_run_counts_them()
    {
        var (gateway, runId, nodeId) = await ANodeRunThatTriedAsync();

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);

        var attempts = record!.Nodes[nodeId].AttemptsMade;
        attempts.Select(attempt => attempt.Number).Should().Equal(1, 2);
        attempts[1].Should().Be(new AttemptRecord(2, Noon.AddMinutes(1), Noon.AddMinutes(1).AddSeconds(1), RunStatus.Failed, "still away"));
        record.Nodes[nodeId].Status.Should().Be(RunStatus.Trying);
    }

    [FactAgainstTheEngine]
    public async Task A_node_run_standing_between_attempts_is_found_on_start()
    {
        var (gateway, runId, nodeId) = await ANodeRunThatTriedAsync();

        var trying = await gateway.TryingNodeRunsAsync(CancellationToken.None);

        trying.Should().Contain(new TryingNodeRun(runId, nodeId, PerItem: false));
    }

    private async Task BindOnceAsync(Role role, Guid played, Guid binds)
    {
        if (await _engine.FindAsync($"{role.Word} role") is not null) return;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, binds, played);
    }
}
