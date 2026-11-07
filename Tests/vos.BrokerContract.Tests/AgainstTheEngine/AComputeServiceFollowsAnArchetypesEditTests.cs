using System.Net.Http.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A compute service's inputs resolve through the study's type chain, so a default edited on the archetype
// moves every study below it. The follower's unit tests answer the membership from a fake; only the engine
// says what a change on an archetype arrives as, and whether the follower maps it back to the study.
public class AComputeServiceFollowsAnArchetypesEditTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public AComputeServiceFollowsAnArchetypesEditTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task An_edit_on_the_archetype_a_study_is_recomputes_the_study()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var studyKind = await _engine.DeclareAsync($"Study kind {Guid.NewGuid():N}", isArchetype: true,
            properties: new Dictionary<string, object?> { ["population"] = 100.0 });
        var study = await _engine.DeclareAsync($"Study {Guid.NewGuid():N}");
        await _engine.RelateAsync(study, isPredicate, studyKind);

        var recomputed = Channel.CreateUnbounded<Guid>();
        var inputs = new RecomputeInputs("ContractCompute",
            () => new HashSet<string>(StringComparer.Ordinal) { "population" },
            (subjectId, _) => { recomputed.Writer.TryWrite(subjectId); return Task.CompletedTask; });
        var log = new AnnouncingLogger<InputChangeRecomputeService>();
        var service = new InputChangeRecomputeService(
            currentToken => new SubscriptionClient(
                _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, tokenProvider: currentToken),
            new ServiceTokenExchange(_engine.ClientFactory, NullLogger<ServiceTokenExchange>.Instance, TheEngine.Url),
            inputs, new DevelopmentEnvironment(), log, startupToken: _engine.AdminToken);
        service.Watch(study);
        await service.StartAsync(CancellationToken.None);
        await log.UntilLoggedAsync("is following its inputs");

        var edited = await _engine.Admin.PutAsJsonAsync($"/api/things/{studyKind}/properties",
            new { name = "population", type = "vos.Double", value = 120.0 });
        edited.EnsureSuccessStatusCode();

        try
        {
            var next = recomputed.Reader.ReadAsync().AsTask();
            await Settle.ForAsync(next, "the study is recomputed after a default on its archetype moved");
            (await next).Should().Be(study);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
