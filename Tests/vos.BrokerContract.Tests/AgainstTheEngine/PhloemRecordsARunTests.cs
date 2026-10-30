using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// Phloem's own gateway, writing a run's record into the real engine.
//
// Its unit tests answer every write with 200 whatever the body, so they agreed with the writer about
// a rule neither of them applied and every case passed while no run in the live system was ever
// recorded. These are the cases that could not have.
public class PhloemRecordsARunTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsARunTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    // What the gateway needs to find before it can write: the built-in predicates by name,
    // and an archetype for each run role, found through the role Thing that binds it.
    //
    // Every case arranges this and the host is shared by the class, so it has to be write-once. A
    // second Thing of the same name makes the name ambiguous, and the gateway resolves its predicates
    // by name — so the arrangement would break the thing it exists to set up.
    private async Task<Guid> AModelThatBindsItsRunRolesAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);

        await BindOnceAsync(PipelineRoles.PipelineRun, "PipelineRun");
        await BindOnceAsync(PipelineRoles.NodeRun, "NodeRun");

        return await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
    }

    private async Task BindOnceAsync(Role role, string archetypeName)
    {
        var archetype = await _engine.DeclareOnceAsync(archetypeName, isArchetype: true);
        var roleName = $"{role.Word} role";
        if (await _engine.FindAsync(roleName) is not null) return;
        await _engine.DeclareAsync(roleName, identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), archetype);
    }

    [FactAgainstTheEngine]
    public async Task A_rehearsal_is_read_back_as_one_and_a_run_for_real_is_not()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
        var rehearsal = Guid.NewGuid();
        var forReal = Guid.NewGuid();

        await Gateway().CreateRunAsync(rehearsal, pipelineId, CancellationToken.None, rehearsal: true);
        await Gateway().CreateRunAsync(forReal, pipelineId, CancellationToken.None);

        (await Gateway().ReadRunRecordAsync(rehearsal, CancellationToken.None))!.Rehearsal.Should().BeTrue();
        (await Gateway().ReadRunRecordAsync(forReal, CancellationToken.None))!.Rehearsal.Should().BeFalse();
    }

    [FactAgainstTheEngine]
    public async Task A_run_is_recorded_with_what_it_started_as()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();

        await Gateway().CreateRunAsync(runId, pipelineId, CancellationToken.None);

        (await _engine.ValueOfAsync(runId, "status")).Should().Be(RunStatus.Running);
        (await _engine.ValueOfAsync(runId, "pipelineId")).Should().Be(pipelineId.ToString());
    }

    // The result is known only when the run ends, and the engine sets only a property the
    // Thing already carries — so publishing it proves the run was created carrying it.
    [FactAgainstTheEngine]
    public async Task A_run_publishes_the_result_it_ends_with()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);
        await gateway.SetRunResultAsync(
            runId, System.Text.Json.JsonDocument.Parse("""{"total":3}""").RootElement, CancellationToken.None);

        (await _engine.ValueOfAsync(runId, "status")).Should().Be(RunStatus.Succeeded);
        (await _engine.ValueOfAsync(runId, ModelNames.Result)).Should().Contain("\"total\":3");
    }

    [FactAgainstTheEngine]
    public async Task A_node_run_is_recorded_and_moves_from_running_to_its_outcome()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
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

    [FactAgainstTheEngine]
    public async Task Each_state_change_is_kept_in_the_history_of_every_property_it_changed()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var nodeName = $"Assess {nodeId:N}";
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Running, null, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Trying, "the service was away", CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Failed, "boom", CancellationToken.None);
        await gateway.SetRunStatusAsync(runId, RunStatus.Failed, CancellationToken.None, "boom");

        var nodeRunId = await _engine.TheOnlyThingNamedAsync($"NodeRun {nodeName}");
        (await ValuesAssertedAsync(nodeRunId, ModelNames.Status))
            .Should().Equal(RunStatus.Running, RunStatus.Trying, RunStatus.Failed);
        (await ValuesAssertedAsync(nodeRunId, ModelNames.Error)).Should().Equal(null, "the service was away", "boom");
        (await ValuesAssertedAsync(runId, ModelNames.Status)).Should().Equal(RunStatus.Running, RunStatus.Failed);
        (await ValuesAssertedAsync(runId, ModelNames.FinishedUtc)).Should().HaveCount(2).And.EndWith(
            (await _engine.ValueOfAsync(runId, ModelNames.FinishedUtc))!);
        (await _engine.ReadAsync(nodeRunId)).GetProperty("Name").GetString().Should().Be($"NodeRun {nodeName}");
    }

    // A browser showing a run watches its node runs through a subscription, and moves a node on the change it is sent.
    [FactAgainstTheEngine]
    public async Task A_subscriber_to_a_node_run_is_sent_its_change_of_status()
    {
        var pipelineId = await AModelThatBindsItsRunRolesAsync();
        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var nodeName = $"Assess {nodeId:N}";
        var gateway = Gateway();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Running, null, CancellationToken.None);
        var nodeRunId = await _engine.TheOnlyThingNamedAsync($"NodeRun {nodeName}");
        var subscription = await (await _engine.Admin.PostAsJsonAsync("/api/subscriptions", new { ids = new[] { nodeRunId } }))
            .Content.ReadFromJsonAsync<JsonElement>();

        await gateway.SetNodeRunStatusAsync(runId, nodeId, nodeName, RunStatus.Succeeded, null, CancellationToken.None);

        var sent = await StreamUntilAsync(
            $"/api/subscriptions/{subscription.GetProperty("subscriptionId").GetString()}/stream?lastEventId={subscription.GetProperty("watermark").GetInt64()}",
            RunStatus.Succeeded);
        sent.Should().Contain("PropertyChanged").And.Contain(nodeRunId.ToString()).And.Contain(RunStatus.Succeeded);
    }

    private async Task<IReadOnlyList<string?>> ValuesAssertedAsync(Guid thingId, string property)
    {
        var facts = await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things/{thingId}/properties/{property}/facts");
        return facts.GetProperty("entries").EnumerateArray()
            .Where(fact => fact.GetProperty("kind").GetString() == "asserted")
            .Select(fact => fact.GetProperty("value") is { ValueKind: JsonValueKind.Null } ? null : fact.GetProperty("value").ToString())
            .ToList();
    }

    private async Task<string> StreamUntilAsync(string path, string expected)
    {
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var answer = await _engine.Admin.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, path), HttpCompletionOption.ResponseHeadersRead, patience.Token);
        answer.EnsureSuccessStatusCode();
        await using var stream = await answer.Content.ReadAsStreamAsync(patience.Token);
        var buffer = new byte[4096];
        var sent = new StringBuilder();
        try
        {
            while (!sent.ToString().Contains(expected))
            {
                var read = await stream.ReadAsync(buffer, patience.Token);
                if (read == 0) break;
                sent.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }
        }
        catch (OperationCanceledException) { }
        return sent.ToString();
    }
}
