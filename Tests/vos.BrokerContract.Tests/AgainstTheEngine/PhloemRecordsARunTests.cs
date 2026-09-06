using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

/// <summary>
/// Phloem's own gateway, writing a run's record into the real engine.
///
/// Its unit tests answer every write with 200 whatever the body, so they agreed with the writer about
/// a rule neither of them applied and every case passed while no run in the live system was ever
/// recorded (Bug #6929). These are the cases that could not have.
/// </summary>
public class PhloemRecordsARunTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsARunTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    /// <summary>What the gateway needs to find before it can write: the built-in predicates by name,
    /// and an archetype for each run role found by the flag it carries.
    ///
    /// Every case arranges this and the host is shared by the class, so it has to be write-once. A
    /// second Thing of the same name makes the name ambiguous, and the gateway resolves its predicates
    /// by name — so the arrangement would break the thing it exists to set up.</summary>
    private async Task<Guid> AModelThatMarksItsRunArchetypesAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of })
            await _engine.DeclareOnceAsync(predicate);

        await _engine.DeclareOnceAsync("PipelineRun", isArchetype: true,
            properties: new Dictionary<string, object?> { [PipelineArchetypes.PipelineRunFlag] = true });
        await _engine.DeclareOnceAsync("NodeRun", isArchetype: true,
            properties: new Dictionary<string, object?> { [PipelineArchetypes.NodeRunFlag] = true });

        return await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
    }

    [Fact]
    public async Task A_run_is_recorded_with_what_it_started_as()
    {
        var pipelineId = await AModelThatMarksItsRunArchetypesAsync();
        var runId = Guid.NewGuid();

        await Gateway().CreateRunAsync(runId, pipelineId, CancellationToken.None);

        (await _engine.ValueOfAsync(runId, "status")).Should().Be(RunStatus.Running);
        (await _engine.ValueOfAsync(runId, "pipelineId")).Should().Be(pipelineId.ToString());
    }

    /// <summary>The result is known only when the run ends, and the engine sets only a property the
    /// Thing already carries — so publishing it proves the run was created carrying it.</summary>
    [Fact]
    public async Task A_run_publishes_the_result_it_ends_with()
    {
        var pipelineId = await AModelThatMarksItsRunArchetypesAsync();
        var runId = Guid.NewGuid();
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);
        await gateway.SetRunResultAsync(
            runId, System.Text.Json.JsonDocument.Parse("""{"total":3}""").RootElement, CancellationToken.None);

        (await _engine.ValueOfAsync(runId, "status")).Should().Be(RunStatus.Succeeded);
        (await _engine.ValueOfAsync(runId, ModelNames.Result)).Should().Contain("\"total\":3");
    }

    [Fact]
    public async Task A_node_run_is_recorded_and_moves_from_running_to_its_outcome()
    {
        var pipelineId = await AModelThatMarksItsRunArchetypesAsync();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Failed, "boom", CancellationToken.None);

        var nodeRunId = await _engine.TheOnlyThingNamedAsync("NodeRun Echo");
        (await _engine.ValueOfAsync(nodeRunId, "status")).Should().Be(RunStatus.Failed);
        (await _engine.ValueOfAsync(nodeRunId, "error")).Should().Be("boom");
        (await _engine.ValueOfAsync(nodeRunId, "nodeId")).Should().Be(nodeId.ToString());
    }
}
