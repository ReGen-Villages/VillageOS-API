using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;
using vos.Service.Shared;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemRecordsWhatStartedARunTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemRecordsWhatStartedARunTests(TheEngine engine) => _engine = engine;

    private sealed record RelationshipRead(Guid? SubjectId, Guid? PredicateId, Guid? TargetId);

    [FactAgainstTheEngine]
    public async Task A_run_another_runs_end_started_reaches_that_run_along_the_predicate_the_startingRun_role_binds()
    {
        foreach (var predicate in new[] { PlatformPredicates.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        await _engine.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        var startedBy = await _engine.PlayingAsync(PipelineRoles.StartingRun, "startedBy");
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));
        var pipeline = await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await gateway.CreateRunAsync(first, pipeline, CancellationToken.None);
        await gateway.CreateRunAsync(second, pipeline, CancellationToken.None, startedBy: first);

        var relationships = await _engine.Admin.GetFromJsonAsync<List<RelationshipRead>>("/api/relationships",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        relationships!.Where(relationship => relationship.SubjectId == second && relationship.PredicateId == startedBy)
            .Should().ContainSingle().Which.TargetId.Should().Be(first);
        relationships.Should().NotContain(relationship => relationship.SubjectId == first && relationship.PredicateId == startedBy);
    }

    [FactAgainstTheEngine]
    public async Task A_run_a_person_started_again_reaches_the_run_it_repeats_along_the_predicate_the_repeatedRun_role_binds()
    {
        foreach (var predicate in new[] { PlatformPredicates.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        await _engine.PlayingAsync(PipelineRoles.PipelineRun, "PipelineRun", isArchetype: true);
        var repeats = await _engine.PlayingAsync(PipelineRoles.RepeatedRun, "repeats");
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));
        var pipeline = await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
        var stopped = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        await gateway.CreateRunAsync(stopped, pipeline, CancellationToken.None);
        await gateway.CreateRunAsync(fresh, pipeline, CancellationToken.None, repeats: stopped);

        var relationships = await _engine.Admin.GetFromJsonAsync<List<RelationshipRead>>("/api/relationships",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        relationships!.Where(relationship => relationship.SubjectId == fresh && relationship.PredicateId == repeats)
            .Should().ContainSingle().Which.TargetId.Should().Be(stopped);
        (await gateway.ReadRunRecordAsync(stopped, CancellationToken.None))!.RepeatedBy.Should().Be(fresh);
        (await gateway.ReadRunRecordAsync(fresh, CancellationToken.None))!.RepeatedBy.Should().BeNull();
    }

}
