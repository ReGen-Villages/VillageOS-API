using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Phloem's gateway holding a wait in the real engine: what it writes for a waiting node run, the vigil it places,
// and the reads a resumed or restarted orchestrator makes. Its unit tests answer every snapshot by matching text
// in the request, so only these show that the engine answers the shapes it asks for.
public class PhloemHoldsAWaitTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemHoldsAWaitTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    private sealed record Village(Guid PipelineId, Guid Plot, Guid Decided);

    // Every case arranges this and the host is shared by the class, so the vocabulary is written once.
    private async Task<Village> AVillageWithAPlotToWaitForAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var isPredicate = await _engine.TheOnlyThingNamedAsync(ModelNames.Is);
        var has = await _engine.TheOnlyThingNamedAsync(ModelNames.Has);

        await BindOnceAsync(PipelineRoles.PipelineRun, await _engine.DeclareOnceAsync("PipelineRun", isArchetype: true));
        await BindOnceAsync(PipelineRoles.NodeRun, await _engine.DeclareOnceAsync("NodeRun", isArchetype: true));
        await BindOnceAsync(PipelineRoles.AwaitedThing, await _engine.DeclareOnceAsync("awaits"));
        await BindOnceAsync(PipelineRoles.HeldVigil, await _engine.DeclareOnceAsync("waitsOn"));

        await _engine.DeclareOnceAsync("Vigil", isArchetype: true, Marked("__IsVigilArchetype"));
        await _engine.DeclareOnceAsync("keptOver", properties: Marked("__IsVigilSubjectPredicate"));
        await _engine.DeclareOnceAsync("notifies", properties: Marked("__IsNotifiedConnectionPredicate"));
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
            await BindOnceAsync(PipelineRoles.RunResumption, resumes);

            var decided = await _engine.DeclareAsync("decided");
            await _engine.RelateAsync(decided, isPredicate, range);
            await _engine.RelateAsync(decided, judges, plotArchetype);
        }

        var plot = await _engine.DeclareAsync($"Plot {Guid.NewGuid():N}");
        await _engine.RelateAsync(plot, isPredicate, plotArchetype);
        return new Village(await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), plot, await _engine.TheOnlyThingNamedAsync("decided"));
    }

    // The marks stay beside the bindings while the platform moves from one to the other, so this runs against both.
    private static IReadOnlyDictionary<string, object?> Marked(string mark) => new Dictionary<string, object?> { [mark] = true };

    private static readonly Role RangeRole = new("range", new("c081de17-c325-55b7-99ca-16fb835ae545"));
    private static readonly Role JudgedThingRole = new("judgedThing", new("63d59765-a9b7-53b8-8a2b-0de3899add18"));

    private async Task BindOnceAsync(Role role, Guid played)
    {
        if (await _engine.FindAsync($"{role.Word} role") is not null) return;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> Noticed =
        new Dictionary<string, JsonElement> { ["notice"] = JsonSerializer.SerializeToElement("sent for review") };

    private async Task<(Guid RunId, Guid NodeId)> AWaitingNodeRunAsync(MyceliumGateway gateway, Village village)
    {
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, village.PipelineId, CancellationToken.None,
            parameters: JsonSerializer.SerializeToElement(new { subject = new { id = village.Plot, name = "Plot 7" } }));
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Review", RunStatus.Waiting, null, CancellationToken.None, outputs: Noticed);
        await gateway.RelateAwaitedAsync(runId, nodeId, village.Plot, CancellationToken.None);
        return (runId, nodeId);
    }

    [FactAgainstTheEngine]
    public async Task A_run_is_read_back_with_its_pipeline_parameters_and_each_node_runs_status_and_outputs()
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
    public async Task A_vigil_placed_over_the_awaited_Thing_is_found_from_the_node_run_holding_it()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);

        var placement = await gateway.PlaceVigilAsync(village.Plot, village.Decided, CancellationToken.None);
        var vigilId = placement.Should().BeOfType<VigilPlacement.Placed>().Subject.VigilId;
        await gateway.RelateHeldVigilAsync(runId, nodeId, vigilId, CancellationToken.None);

        (await gateway.HolderOfVigilAsync(vigilId, CancellationToken.None)).Should().Be(new VigilHolder(runId, nodeId));
        (await gateway.IsVigilLiveAsync(vigilId, CancellationToken.None)).Should().BeTrue();
        var vigil = await _engine.ReadAsync(vigilId);
        vigil.GetProperty("Name").GetString().Should().Contain("decided");
    }

    [FactAgainstTheEngine]
    public async Task The_platform_refuses_a_vigil_over_a_Thing_the_range_does_not_judge()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var stranger = await _engine.DeclareAsync($"Stranger {Guid.NewGuid():N}");

        var placement = await Gateway().PlaceVigilAsync(stranger, village.Decided, CancellationToken.None);

        placement.Should().BeOfType<VigilPlacement.Refused>().Which.Reason.Should().Contain("does not judge");
    }

    [FactAgainstTheEngine]
    public async Task A_restarted_orchestrator_finds_each_waiting_node_run_with_its_Thing_and_vigil()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);
        var vigilId = ((VigilPlacement.Placed)await gateway.PlaceVigilAsync(village.Plot, village.Decided, CancellationToken.None)).VigilId;
        await gateway.RelateHeldVigilAsync(runId, nodeId, vigilId, CancellationToken.None);

        var waits = await Gateway().WaitingNodeRunsAsync(CancellationToken.None);

        waits.Should().ContainSingle(wait => wait.RunId == runId).Which.Should().Be(new HeldWait(runId, nodeId, village.Plot, vigilId));
    }

    [FactAgainstTheEngine]
    public async Task A_resumed_node_run_forgets_its_Thing_and_a_vigil_held_again_replaces_the_one_before()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var (runId, nodeId) = await AWaitingNodeRunAsync(gateway, village);
        var first = ((VigilPlacement.Placed)await gateway.PlaceVigilAsync(village.Plot, village.Decided, CancellationToken.None)).VigilId;
        var second = ((VigilPlacement.Placed)await gateway.PlaceVigilAsync(village.Plot, village.Decided, CancellationToken.None)).VigilId;
        await gateway.RelateHeldVigilAsync(runId, nodeId, first, CancellationToken.None);

        await gateway.RelateHeldVigilAsync(runId, nodeId, second, CancellationToken.None);
        await gateway.ForgetAwaitedAsync(runId, nodeId, CancellationToken.None);

        (await gateway.HolderOfVigilAsync(first, CancellationToken.None)).Should().BeNull();
        (await gateway.HolderOfVigilAsync(second, CancellationToken.None)).Should().Be(new VigilHolder(runId, nodeId));
        (await gateway.WaitingNodeRunsAsync(CancellationToken.None)).Single(wait => wait.RunId == runId).AwaitedId.Should().BeNull();
    }

    [FactAgainstTheEngine]
    public async Task A_vigil_the_platform_has_retired_is_no_longer_live()
    {
        var village = await AVillageWithAPlotToWaitForAsync();
        var gateway = Gateway();
        var vigilId = ((VigilPlacement.Placed)await gateway.PlaceVigilAsync(village.Plot, village.Decided, CancellationToken.None)).VigilId;

        (await _engine.Admin.DeleteAsync($"/api/vigils/{vigilId}")).EnsureSuccessStatusCode();

        (await gateway.IsVigilLiveAsync(vigilId, CancellationToken.None)).Should().BeFalse();
    }
}
