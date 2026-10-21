using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A stop request as the engine holds it: its kind declares the reason, as the shared site template does, and the
// runs it stops are read off the run-subject relationships of what it names.
public class PhloemStopsRunsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemStopsRunsTests(TheEngine engine) => _engine = engine;

    private MyceliumGateway Gateway() => new(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

    private async Task<Guid> BindOnceAsync(Role role, Func<Task<Guid>> declare)
    {
        var binds = await _engine.TheOnlyThingNamedAsync(ModelNames.Binds);
        if (await _engine.FindAsync($"{role.Word} role") is not null)
            return await _engine.TheOnlyThingNamedAsync($"{role.Word} bound");
        var played = await declare();
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, binds, played);
        return played;
    }

    private async Task<(Guid IsPredicate, Guid StopKind)> AModelThatStopsRunsAsync()
    {
        foreach (var predicate in new[] { ModelNames.Is, ModelNames.Has, ModelNames.Of, ModelNames.Binds })
            await _engine.DeclareOnceAsync(predicate);
        await BindOnceAsync(PipelineRoles.PipelineRun, () => _engine.DeclareAsync($"{PipelineRoles.PipelineRun.Word} bound", isArchetype: true));
        await BindOnceAsync(PipelineRoles.RunSubject, () => _engine.DeclareAsync($"{PipelineRoles.RunSubject.Word} bound"));
        await BindOnceAsync(PipelineRoles.StoppedBy, () => _engine.DeclareAsync($"{PipelineRoles.StoppedBy.Word} bound"));
        var stopKind = await BindOnceAsync(PipelineRoles.StopRequest, async () =>
        {
            var id = Guid.NewGuid();
            var written = await _engine.Admin.PostAsJsonAsync("/api/things", new
            {
                Id = id,
                Name = $"{PipelineRoles.StopRequest.Word} bound",
                IsArchetype = true,
                Properties = new Dictionary<string, object> { [ModelNames.Reason] = new { typeInfo = "vos.String", value = "" } },
            });
            written.EnsureSuccessStatusCode();
            return id;
        });
        return (await _engine.TheOnlyThingNamedAsync(ModelNames.Is), stopKind);
    }

    [FactAgainstTheEngine]
    public async Task A_stop_request_is_read_with_its_reason_and_the_runs_about_what_it_names()
    {
        var (isPredicate, stopKind) = await AModelThatStopsRunsAsync();
        var gateway = Gateway();
        var site = await _engine.DeclareAsync($"Site {Guid.NewGuid():N}");
        var pipeline = await _engine.DeclareAsync($"Pipeline {Guid.NewGuid():N}");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await gateway.CreateRunAsync(first, pipeline, CancellationToken.None, subject: new RunSubject(site, "the site"));
        await gateway.CreateRunAsync(second, pipeline, CancellationToken.None, subject: new RunSubject(site, "the site"));
        await gateway.SetRunStatusAsync(second, RunStatus.Succeeded, CancellationToken.None);
        var request = await _engine.DeclareAsync($"Stop request {Guid.NewGuid():N}");
        await _engine.RelateAsync(request, isPredicate, stopKind);
        (await _engine.Admin.PutAsJsonAsync($"/api/things/{request}/properties",
            new { name = ModelNames.Reason, type = "vos.String", value = "site withdrawn by its owner" })).EnsureSuccessStatusCode();

        var stop = await gateway.StopRequestedAsync(request, site, CancellationToken.None);
        await gateway.RelateStoppedByAsync(first, request, CancellationToken.None);
        await gateway.RetireVigilAsync(Guid.NewGuid(), CancellationToken.None);

        stop.Reason.Should().Be("site withdrawn by its owner");
        stop.Runs.Should().Equal(first);
        (await gateway.StopRequestedAsync(request, first, CancellationToken.None)).Runs.Should().Equal(first);
    }
}
