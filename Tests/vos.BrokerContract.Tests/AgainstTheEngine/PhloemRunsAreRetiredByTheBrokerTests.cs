using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Tests.Shared;
using Xunit;
using static vos.BrokerContract.Tests.AgainstTheEngine.EngineArrangement;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemRunsAreRetiredByTheBrokerTests : IClassFixture<TheEngineRetiringEachSecond>
{
    private static readonly Role StateRecordRole = new("stateRecord", RoleIdentity.Of("stateRecord"));
    private static readonly Role RetiringStateRole = new("retiringState", RoleIdentity.Of("retiringState"));
    private static readonly Role HoldingRole = new("holding", RoleIdentity.Of("holding"));

    private const string EndedWell = "RunEndedWell";
    private static readonly TimeSpan Period = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly TheEngine _engine;

    public PhloemRunsAreRetiredByTheBrokerTests(TheEngineRetiringEachSecond engine) => _engine = engine.Engine;

    // The run kind declares what the shared site template declares, with a period of an hour.
    private async Task<MyceliumGateway> AGatewayAsync()
    {
        var @is = await _engine.DeclareOnceAsync(PlatformPredicates.Is);
        foreach (var predicate in new[] { ModelNames.Of, ModelNames.Binds }) await _engine.DeclareOnceAsync(predicate);
        await _engine.PlayingAsync(HoldingRole, ModelNames.Has);
        var runKind = await _engine.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.NodeRun, "NodeRun", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.Attempt, "Attempt", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.Orchestrator, "Phloem prototype", isArchetype: true);
        await _engine.PlayingAsync(StateRecordRole, "heldState");
        var retiredFrom = await _engine.PlayingAsync(RetiringStateRole, "retiredFrom");
        var range = await _engine.PlayingAsync(RangeRole, "Range", isArchetype: true);
        var judges = await _engine.PlayingAsync(JudgedThingRole, "judges");

        if (await _engine.FindAsync(EndedWell) is null)
        {
            var endedWell = await _engine.DeclareAsync(EndedWell, properties: new Dictionary<string, object?>
            {
                ["Criteria"] = $"status IN ('{RunStatus.Succeeded}', '{RunStatus.Cancelled}')", ["KeepsStateHistory"] = true,
            });
            await _engine.RelateAsync(endedWell, @is, range);
            await _engine.RelateAsync(endedWell, judges, runKind);
            await DeclareThePeriodAsync(await RelationshipAsync(runKind, retiredFrom, endedWell));
        }

        var token = await _engine.ServiceTokenAsync("pipeline engine");
        return new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(token));
    }

    private async Task<Guid> RelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId)
    {
        var related = await _engine.Admin.PostAsJsonAsync("/api/relationships", new { subjectId, predicateId, targetId });
        related.EnsureSuccessStatusCode();
        return Field(await related.Content.ReadFromJsonAsync<JsonElement>(), "id").GetGuid();
    }

    private async Task DeclareThePeriodAsync(Guid declaration)
    {
        var seconds = Period.TotalSeconds;
        var written = await _engine.Admin.PutAsJsonAsync($"/api/relationships/{declaration}/properties",
            new { Name = "afterSeconds", Type = TypedProperties.TypeNameFor(seconds), Value = seconds });
        written.EnsureSuccessStatusCode();
    }

    private async Task MoveTheModelClockPastThePeriodAsync()
    {
        var now = Field(await _engine.Admin.GetFromJsonAsync<JsonElement>("/api/time"), "now").GetDateTimeOffset();
        (await _engine.Admin.PostAsJsonAsync("/api/time", new { Now = now + Period + TimeSpan.FromMinutes(1) })).EnsureSuccessStatusCode();
    }

    private async Task<HashSet<Guid>> ThingsOfKindAsync(string kind) =>
        (await _engine.Admin.GetFromJsonAsync<JsonElement>($"/api/things?type={kind}")).EnumerateArray()
            .Select(thing => Field(thing, "id").GetGuid()).ToHashSet();

    private async Task<bool> StandsAsync(Guid identifier) =>
        (await _engine.Admin.GetAsync($"/api/things/{identifier}")).StatusCode != HttpStatusCode.NotFound;

    private static JsonElement Field(JsonElement answer, string name) =>
        answer.EnumerateObject().First(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private async Task<Guid> ARunWithANodeRunAndAnAttemptAsync(MyceliumGateway gateway)
    {
        var runId = Guid.NewGuid();
        var node = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}"), CancellationToken.None);
        await gateway.SetNodeRunStatusAsync(runId, node, "Box", RunStatus.Succeeded, null, CancellationToken.None);
        await gateway.RecordAttemptAsync(runId, node, null, new AttemptRecord(1, Noon, Noon.AddSeconds(1), RunStatus.Succeeded, null), CancellationToken.None);
        return runId;
    }

    [FactAgainstTheEngine]
    public async Task A_run_that_succeeded_is_retired_by_the_broker_with_its_node_run_and_attempt_once_its_period_has_passed()
    {
        var gateway = await AGatewayAsync();
        var heldBefore = (await ThingsOfKindAsync("NodeRun")).Concat(await ThingsOfKindAsync("Attempt")).ToHashSet();
        var runId = await ARunWithANodeRunAndAnAttemptAsync(gateway);
        await gateway.SetRunStatusAsync(runId, RunStatus.Succeeded, CancellationToken.None);
        var itsOwn = (await ThingsOfKindAsync("NodeRun")).Concat(await ThingsOfKindAsync("Attempt")).Except(heldBefore).ToList();

        await MoveTheModelClockPastThePeriodAsync();

        await Settle.UntilAsync(async () => !await StandsAsync(runId), "the broker retires the run once its period has passed");
        itsOwn.Should().HaveCount(2, "the run's node run and its attempt");
        foreach (var thing in itsOwn) (await StandsAsync(thing)).Should().BeFalse("a run's node run and attempt go with it");
    }

    [FactAgainstTheEngine]
    public async Task A_run_still_running_is_not_retired()
    {
        var gateway = await AGatewayAsync();
        var runId = await ARunWithANodeRunAndAnAttemptAsync(gateway);

        await MoveTheModelClockPastThePeriodAsync();
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromSeconds(3));

        (await StandsAsync(runId)).Should().BeTrue();
    }
}
