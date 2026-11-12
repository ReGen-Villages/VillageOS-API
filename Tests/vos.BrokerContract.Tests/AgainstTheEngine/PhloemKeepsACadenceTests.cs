using System.Net.Http.Json;
using FluentAssertions;
using vos.Service.Shared;
using vos.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A cadence as the engine holds it: its kind declares the interval and the instant last due, as the shared site
// template does, so the instant the orchestrator writes lands on a name the cadence inherits rather than states.
public class PhloemKeepsACadenceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemKeepsACadenceTests(TheEngine engine) => _engine = engine;

    private async Task<(MyceliumGateway Gateway, Guid CadenceId)> AnHourlyCadenceAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        var isPredicate = await _engine.TheOnlyThingNamedAsync(ModelNames.Is);
        var binds = await _engine.TheOnlyThingNamedAsync(ModelNames.Binds);
        var kind = await _engine.FindAsync("Cadence") ?? await DeclareTheKindAsync();
        if (await _engine.FindAsync($"{PipelineRoles.Cadence.Word} role") is null)
        {
            await _engine.DeclareAsync($"{PipelineRoles.Cadence.Word} role", identifier: PipelineRoles.Cadence.Id);
            await _engine.RelateAsync(PipelineRoles.Cadence.Id, binds, kind);
        }
        var cadenceId = await _engine.DeclareAsync($"Every hour {Guid.NewGuid():N}");
        await _engine.RelateAsync(cadenceId, isPredicate, kind);
        var interval = await _engine.Admin.PutAsJsonAsync($"/api/things/{cadenceId}/properties",
            new { name = ModelNames.IntervalMinutes, type = "vos.Double", value = "60" });
        interval.EnsureSuccessStatusCode();
        return (new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken)), cadenceId);
    }

    private async Task<Guid> DeclareTheKindAsync()
    {
        var id = Guid.NewGuid();
        var written = await _engine.Admin.PostAsJsonAsync("/api/things", new
        {
            Id = id,
            Name = "Cadence",
            IsArchetype = true,
            Properties = new Dictionary<string, object>
            {
                [ModelNames.IntervalMinutes] = new { typeInfo = "vos.Double", value = (double?)null },
                [ModelNames.LastDue] = new { typeInfo = "vos.DateTime", value = (string?)null },
            },
        });
        written.EnsureSuccessStatusCode();
        return id;
    }

    [FactAgainstTheEngine]
    public async Task A_cadence_is_read_with_its_interval_and_no_instant_until_the_orchestrator_marks_it_due()
    {
        var (gateway, cadenceId) = await AnHourlyCadenceAsync();
        await using var _ = gateway;
        var noon = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        var before = (await gateway.CadencesAsync(CancellationToken.None)).Single(cadence => cadence.Id == cadenceId);
        await gateway.SetLastDueAsync(cadenceId, noon, CancellationToken.None);
        var after = (await gateway.CadencesAsync(CancellationToken.None)).Single(cadence => cadence.Id == cadenceId);

        before.IntervalMinutes.Should().Be(60);
        before.LastDue.Should().BeNull();
        after.LastDue.Should().Be(noon);
    }

    [FactAgainstTheEngine]
    public async Task The_kind_of_cadence_is_not_read_as_one()
    {
        var (gateway, _) = await AnHourlyCadenceAsync();
        await using var __ = gateway;

        (await gateway.CadencesAsync(CancellationToken.None)).Should().NotContain(cadence => cadence.Name == "Cadence");
    }

    [FactAgainstTheEngine]
    public async Task A_cadence_edited_after_the_subscription_opened_and_one_typed_in_later_reach_the_held_cadences()
    {
        var (gateway, cadenceId) = await AnHourlyCadenceAsync();
        await using var _ = gateway;
        (await gateway.CadencesAsync(CancellationToken.None)).Single(cadence => cadence.Id == cadenceId).IntervalMinutes.Should().Be(60);

        var edited = await _engine.Admin.PutAsJsonAsync($"/api/things/{cadenceId}/properties",
            new { name = ModelNames.IntervalMinutes, type = "vos.Double", value = "30" });
        edited.EnsureSuccessStatusCode();
        await Settle.UntilAsync(() => Held(gateway).Single(cadence => cadence.Id == cadenceId).IntervalMinutes == 30, "the edited interval reaches the held cadence");

        var later = await _engine.DeclareAsync($"Every day {Guid.NewGuid():N}");
        await _engine.RelateAsync(later, await _engine.TheOnlyThingNamedAsync(ModelNames.Is), await _engine.TheOnlyThingNamedAsync("Cadence"));
        await Settle.UntilAsync(() => Held(gateway).Any(cadence => cadence.Id == later), "the cadence typed in later is held");
    }

    private static IReadOnlyList<Cadence> Held(MyceliumGateway gateway) => gateway.CadencesAsync(CancellationToken.None).GetAwaiter().GetResult();
}
