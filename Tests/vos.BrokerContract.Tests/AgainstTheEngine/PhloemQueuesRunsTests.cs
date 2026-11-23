using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemQueuesRunsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemQueuesRunsTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));

    private async Task<Guid> APipelineInAModelThatBindsItsRunRolesAsync()
    {
        foreach (var predicate in new[] { PlatformPredicates.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        await _engine.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.NodeRun, "NodeRun", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);
        return await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
    }

    [FactAgainstTheEngine]
    public async Task A_queued_run_is_read_back_with_its_number_until_it_begins()
    {
        var pipelineId = await APipelineInAModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();
        await Gateway().CreateRunAsync(runId, pipelineId, CancellationToken.None, queueNumber: 42);
        var queuedAtFirst = await Gateway().QueuedRunsAsync(CancellationToken.None);

        await Gateway().BeginQueuedRunAsync(runId, CancellationToken.None);

        queuedAtFirst.Should().Contain(new QueuedRun(runId, pipelineId, 42));
        (await Gateway().ReadRunRecordAsync(runId, CancellationToken.None))!.Status.Should().Be(RunStatus.Running);
        (await Gateway().QueuedRunsAsync(CancellationToken.None)).Should().NotContain(queued => queued.RunId == runId);
    }

    [FactAgainstTheEngine]
    public async Task A_queued_run_whose_pipeline_is_deleted_still_names_it_so_it_can_be_ended()
    {
        var pipelineId = await APipelineInAModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();
        await Gateway().CreateRunAsync(runId, pipelineId, CancellationToken.None, queueNumber: 7);

        (await _engine.Admin.DeleteAsync($"/api/things/{pipelineId}")).EnsureSuccessStatusCode();

        (await Gateway().QueuedRunsAsync(CancellationToken.None)).Should().Contain(new QueuedRun(runId, pipelineId, 7));
        (await Gateway().ReadRunRecordAsync(runId, CancellationToken.None))!.PipelineId.Should().Be(pipelineId);
    }
}
