using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemRunsAreRetiredByTheBrokerTests : IClassFixture<TheEngineRetiringEachSecond>
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly TheEngine _engine;

    public PhloemRunsAreRetiredByTheBrokerTests(TheEngineRetiringEachSecond engine) => _engine = engine.Engine;

    private async Task<MyceliumGateway> AGatewayAsync()
    {
        var token = await _engine.ServiceTokenAsync("pipeline engine");
        return new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(token));
    }

    private async Task MoveTheModelClockPastThePeriodAsync()
    {
        var now = Field(await _engine.Admin.GetFromJsonAsync<JsonElement>("/api/time"), "now").GetDateTimeOffset();
        (await _engine.Admin.PostAsJsonAsync("/api/time", new { Now = now + RunRetirementSeed.Period + TimeSpan.FromMinutes(1) })).EnsureSuccessStatusCode();
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
