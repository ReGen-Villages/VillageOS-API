using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A Thing never holds its own value and an inherited value of one name, so the engine refuses to relate an
// attempt or a message that already owns a property to a kind that declares it; each kind here declares one.
public class PhloemRecordsAnAttemptAndAMessageWhoseKindsDeclareTheirPropertiesTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsAnAttemptAndAMessageWhoseKindsDeclareTheirPropertiesTests(TheEngine engine) => _engine = engine;

    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private MyceliumGateway Gateway() => new(
        _engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    private async Task<Guid> ARunWhoseKindsDeclareTheirPropertiesAsync(MyceliumGateway gateway)
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);

        await BindOnceAsync(PipelineRoles.PipelineRun, "RunKind", ModelNames.Status);
        await BindOnceAsync(PipelineRoles.NodeRun, "NodeRunKind", ModelNames.Status);
        await BindOnceAsync(PipelineRoles.Attempt, "AttemptKind", ModelNames.Status);
        await BindOnceAsync(PipelineRoles.SentMessage, "SentMessageKind", ModelNames.Payload);

        var runId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), CancellationToken.None);
        return runId;
    }

    private async Task BindOnceAsync(Role role, string archetypeName, string declaredProperty)
    {
        var archetype = await _engine.DeclareOnceAsync(
            archetypeName, isArchetype: true, new Dictionary<string, object?> { [declaredProperty] = "" });
        if (await _engine.FindAsync(role.Word) is not null) return;
        await _engine.BindAsync(role, archetype);
    }

    [FactAgainstTheEngine]
    public async Task An_attempt_is_recorded_under_its_node_run()
    {
        var gateway = Gateway();
        var runId = await ARunWhoseKindsDeclareTheirPropertiesAsync(gateway);
        var nodeId = Guid.NewGuid();
        await gateway.SetNodeRunStatusAsync(runId, nodeId, "Echo", RunStatus.Running, null, CancellationToken.None);

        await gateway.RecordAttemptAsync(runId, nodeId, null,
            new AttemptRecord(1, Noon, Noon.AddSeconds(1), RunStatus.Failed, "the service was away"), CancellationToken.None);

        var record = await gateway.ReadRunRecordAsync(runId, CancellationToken.None);
        record!.Nodes[nodeId].AttemptsMade.Should().Equal(
            new AttemptRecord(1, Noon, Noon.AddSeconds(1), RunStatus.Failed, "the service was away"));
    }

    [FactAgainstTheEngine]
    public async Task A_message_is_recorded_with_its_payload_held_as_its_kinds_override()
    {
        var gateway = Gateway();
        var runId = await ARunWhoseKindsDeclareTheirPropertiesAsync(gateway);
        var system = await _engine.DeclareAsync($"System {Guid.NewGuid():N}");
        var connection = await _engine.DeclareAsync($"Connection {Guid.NewGuid():N}");
        var telling = new Telling(connection, "Reporting office", system);

        await gateway.SendMessageAsync(runId, telling, JsonDocument.Parse("""{"result":"world"}""").RootElement, CancellationToken.None);

        var message = await _engine.ReadAsync(await _engine.TheOnlyThingNamedAsync($"Message to Reporting office {runId:N}"));
        var payload = message.GetProperty("InheritedOverrides").EnumerateObject().Single().Value
            .GetProperty("Properties").GetProperty(ModelNames.Payload).GetProperty("value").GetString();
        JsonDocument.Parse(payload!).RootElement.GetProperty("result").GetString().Should().Be("world");
    }
}
