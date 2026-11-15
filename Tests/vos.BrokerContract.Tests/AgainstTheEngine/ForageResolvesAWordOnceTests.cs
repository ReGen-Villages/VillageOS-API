using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The second run plans from the read the first made, so it repeats every removal and write the first has made.
public class ForageResolvesAWordOnceTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public ForageResolvesAWordOnceTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_word_whose_stale_relationship_another_run_already_removed_is_resolved()
    {
        var spring = await ArrangeSpringAsync("Willow Bend spring");
        await _engine.RelateAsync(spring, await _engine.TheOnlyThingNamedAsync("flowsAs"),
            await _engine.TheOnlyThingNamedAsync("flashy"));

        await TwoRunsFromOneReadAsync(spring);

        (await RegimesOfAsync(spring)).Should().Equal(await _engine.TheOnlyThingNamedAsync("steady"));
    }

    [FactAgainstTheEngine]
    public async Task A_word_with_no_stale_relationship_is_resolved_by_both_runs()
    {
        var spring = await ArrangeSpringAsync("Birch Ridge spring");

        await TwoRunsFromOneReadAsync(spring);

        (await RegimesOfAsync(spring)).Should().Equal(await _engine.TheOnlyThingNamedAsync("steady"));
    }

    private async Task TwoRunsFromOneReadAsync(Guid spring)
    {
        var oneRead = new ReadOnce(Subscriptions());
        var logger = new CapturingLogger<VocabularyEdgeWriter>();
        var writer = new VocabularyEdgeWriter(oneRead,
            new MyceliumRelationshipClient(_engine.ClientFactory, NullLogger<MyceliumRelationshipClient>.Instance,
                TheEngine.Url, new LaunchToken(_engine.AdminToken)),
            logger);
        var report = new DiscoveryReport(
            [new SourceOutcome("flow-survey", true, null, SubjectId: spring,
                Written: new Dictionary<string, string> { ["flowRegime"] = "steady" })],
            []);

        foreach (var run in new[] { "first", "second" })
        {
            var resolved = await writer.ResolveAsync(report, default);
            resolved!.NotWritten.Should().BeEmpty($"the {run} run's replacement is already in the model or is written now");
            resolved.Written.Should().ContainSingle().Which.Word.Should().Be("steady");
        }

        oneRead.Reads.Should().Be(1, "the second run planned from the read the first run made");
        logger.ErrorLines.Should().BeEmpty();
    }

    private async Task<IReadOnlyList<Guid>> RegimesOfAsync(Guid spring)
    {
        var flowsAs = await _engine.TheOnlyThingNamedAsync("flowsAs");
        var read = await Subscriptions().ReadSnapshotAsync(DiscoveredVocabularyResolver.SelectorFor([spring]));
        return [.. read.Snapshot.Relationships
            .Where(edge => edge.SubjectId == spring && edge.PredicateId == flowsAs)
            .Select(edge => edge.TargetId)];
    }

    // Declared once per engine, because a second set of members would make each word name two of them.
    private async Task<Guid> ArrangeSpringAsync(string name)
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        if (await _engine.FindAsync("FlowRegime") is null)
        {
            var kind = await _engine.DeclareAsync("Vocabulary kind", isArchetype: true);
            await _engine.BindAsync(DiscoveredVocabularyResolver.DiscoveredVocabularyRole, kind);
            var regime = await _engine.DeclareAsync("FlowRegime", isArchetype: true,
                new Dictionary<string, object?> { [DiscoveredVocabularyResolver.ResolvedFromProperty] = "flowRegime" });
            await _engine.RelateAsync(regime, isPredicate, kind);
            var springKind = await _engine.DeclareAsync("Spring", isArchetype: true);
            await _engine.RelateAsync(springKind, await _engine.DeclareAsync("flowsAs"), regime);
            await _engine.RelateAsync(await _engine.DeclareAsync("steady"), isPredicate, regime);
            await _engine.RelateAsync(await _engine.DeclareAsync("flashy"), isPredicate, regime);
        }

        var spring = await _engine.DeclareAsync(name);
        await _engine.RelateAsync(spring, isPredicate, await _engine.TheOnlyThingNamedAsync("Spring"));
        return spring;
    }

    private SubscriptionClient Subscriptions() => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, new LaunchToken(_engine.AdminToken));

    private sealed class ReadOnce(ISubscriptionClient engine) : ISubscriptionClient
    {
        private SubscribeResult? _first;

        public int Reads { get; private set; }

        public event Action? Reconnected { add { } remove { } }

        public async Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
        {
            if (_first is null)
            {
                _first = await engine.SubscribeAsync(selector, ct);
                Reads++;
            }
            return _first;
        }

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            engine.ReadSnapshotAsync(selector, ct);

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default) =>
            engine.AddObjectsAsync(subscriptionId, selector, ct);

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            engine.RemoveObjectsAsync(subscriptionId, objectIds, ct);

        public IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long fromSequence, CancellationToken ct = default) =>
            engine.StreamAsync(subscriptionId, fromSequence, ct);
    }
}
