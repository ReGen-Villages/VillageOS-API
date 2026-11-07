using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A Thing never holds its own value and an inherited value of one name, so the engine refuses to relate a
// run that already owns `status` to a kind that declares it; each case here uses such a kind.
public class PhloemRecordsARunWhoseKindDeclaresItsStatusTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsARunWhoseKindDeclaresItsStatusTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    private async Task<Guid> AModelWhoseRunKindsDeclareTheirStatusAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);

        await BindOnceAsync(PipelineRoles.PipelineRun, "RunKind");
        await BindOnceAsync(PipelineRoles.NodeRun, "NodeRunKind");

        return await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
    }

    private async Task BindOnceAsync(Role role, string archetypeName)
    {
        var archetype = await _engine.DeclareOnceAsync(
            archetypeName, isArchetype: true, new Dictionary<string, object?> { [ModelNames.Status] = "" });
        if (await _engine.FindAsync(role.Word) is not null) return;
        await _engine.BindAsync(role, archetype);
    }

    [FactAgainstTheEngine]
    public async Task A_run_is_recorded_and_belongs_to_its_pipeline()
    {
        var pipelineId = await AModelWhoseRunKindsDeclareTheirStatusAsync();
        var runId = Guid.NewGuid();

        await Gateway().CreateRunAsync(runId, pipelineId, CancellationToken.None);

        var record = await Gateway().ReadRunRecordAsync(runId, CancellationToken.None);
        record.Should().NotBeNull("the run is related to its pipeline");
        record!.PipelineId.Should().Be(pipelineId);
        record.Status.Should().Be(RunStatus.Running);
    }

    [FactAgainstTheEngine]
    public async Task A_node_run_is_recorded_under_its_run_and_moves_to_its_outcome()
    {
        var pipelineId = await AModelWhoseRunKindsDeclareTheirStatusAsync();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Succeeded, null, CancellationToken.None);

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);
        record!.Nodes.Should().ContainKey(nodeId);
        record.Nodes[nodeId].Status.Should().Be(RunStatus.Succeeded);
    }
}
