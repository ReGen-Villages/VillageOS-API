using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The read a start makes is answered by the engine itself, so the arriving Thing's kind and system, and every Thing a
// start node stands for, come back as the engine selects them.
public class PhloemStartsTheMostSpecificPipelineTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemStartsTheMostSpecificPipelineTests(TheEngine engine) => _engine = engine;

    private sealed record Arrivals(Guid Connection, Guid Kind, Guid WaterBoard, Guid Other, Guid Standard, Guid WaterBoardReadings, Guid OfKind, Guid From);

    private async Task BindOnceAsync(Role role, Guid played)
    {
        if (await _engine.FindAsync($"{role.Word} role") is not null) return;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
    }

    private async Task<Guid> MemberAsync(string name, Guid archetype)
    {
        var member = await _engine.DeclareAsync(name);
        await _engine.RelateAsync(member, await _engine.TheOnlyThingNamedAsync(ModelNames.Is), archetype);
        return member;
    }

    private async Task<Guid> PublishedAsync(string name, params Guid[] standsFor)
    {
        var isPredicate = await _engine.TheOnlyThingNamedAsync(ModelNames.Is);
        var has = await _engine.TheOnlyThingNamedAsync(ModelNames.Has);
        var pipeline = await _engine.DeclareAsync(name, properties: new Dictionary<string, object?> { [ModelNames.Published] = true });
        await _engine.RelateAsync(pipeline, isPredicate, await _engine.TheOnlyThingNamedAsync("Pipeline"));
        var start = await _engine.DeclareAsync($"{name} · start");
        await _engine.RelateAsync(start, isPredicate, await _engine.TheOnlyThingNamedAsync("PipelineInput"));
        await _engine.RelateAsync(pipeline, has, start);
        foreach (var stood in standsFor)
            await _engine.RelateAsync(start, await _engine.TheOnlyThingNamedAsync("standsFor"), stood);
        return pipeline;
    }

    private async Task<Arrivals> AConnectionWithAStandardAndAWaterBoardPipelineAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        await BindOnceAsync(PipelineRoles.Pipeline, await _engine.DeclareOnceAsync("Pipeline", isArchetype: true));
        await BindOnceAsync(PipelineRoles.PipelineInput, await _engine.DeclareOnceAsync("PipelineInput", isArchetype: true));
        await BindOnceAsync(PipelineRoles.StandsFor, await _engine.DeclareOnceAsync("standsFor"));
        var connectionArchetype = await _engine.DeclareOnceAsync("PlatformServiceConnection", isArchetype: true);
        await BindOnceAsync(PipelineRoles.Connection, connectionArchetype);
        var kindArchetype = await _engine.DeclareOnceAsync("MessageKind", isArchetype: true);
        await BindOnceAsync(PipelineRoles.MessageKind, kindArchetype);
        var systemArchetype = await _engine.DeclareOnceAsync("ExternalSystem", isArchetype: true);
        await BindOnceAsync(PipelineRoles.ExternalSystem, systemArchetype);

        var run = Guid.NewGuid().ToString("N")[..8];
        var connection = await MemberAsync($"documents arrive {run}", connectionArchetype);
        var kind = await MemberAsync($"meter reading {run}", kindArchetype);
        var waterBoard = await MemberAsync($"water board system {run}", systemArchetype);
        var other = await MemberAsync($"another system {run}", systemArchetype);
        var standard = await PublishedAsync($"standard {run}", connection);
        var waterBoardReadings = await PublishedAsync($"the water board's meter readings {run}", connection, kind, waterBoard);
        return new Arrivals(connection, kind, waterBoard, other, standard, waterBoardReadings,
            await _engine.DeclareAsync($"ofKind {run}"), await _engine.DeclareAsync($"from {run}"));
    }

    private async Task<StartResolution> ArrivingAsync(Arrivals arrivals, Guid system)
    {
        var document = await _engine.DeclareAsync($"an arriving document {Guid.NewGuid():N}");
        await _engine.RelateAsync(document, arrivals.OfKind, arrivals.Kind);
        await _engine.RelateAsync(document, arrivals.From, system);
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);
        return await new PipelineStarter(gateway).ResolveAsync(arrivals.Connection, null, document, CancellationToken.None);
    }

    [FactAgainstTheEngine]
    public async Task A_message_from_the_named_system_starts_the_specific_pipeline()
    {
        var arrivals = await AConnectionWithAStandardAndAWaterBoardPipelineAsync();

        (await ArrivingAsync(arrivals, arrivals.WaterBoard)).PipelineId.Should().Be(arrivals.WaterBoardReadings);
    }

    [FactAgainstTheEngine]
    public async Task A_message_from_any_other_system_starts_the_general_pipeline()
    {
        var arrivals = await AConnectionWithAStandardAndAWaterBoardPipelineAsync();

        (await ArrivingAsync(arrivals, arrivals.Other)).PipelineId.Should().Be(arrivals.Standard);
    }
}
