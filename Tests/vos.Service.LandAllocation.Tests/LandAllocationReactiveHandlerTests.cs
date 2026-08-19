using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.LandAllocation.Services;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.LandAllocation.Tests;

// The handler between the two: read the split, compute, and write each result where it belongs. What it
// decides — which Thing takes which output, what an uncategorised allocation does, and what it reports as
// the Things it read from — is not arithmetic and is not in the reader, so it is pinned here.
public class LandAllocationReactiveHandlerTests
{
    private static readonly Guid Study = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Site = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Parcel = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Housing = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Growing = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private sealed record Written(string Uri, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public readonly List<Written> Writes = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("/facts"))
                Writes.Add(new Written(request.RequestUri.AbsolutePath,
                    request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return responder(request);
        }
    }

    private sealed class StubSubscriptions(SnapshotDocument snapshot) : ISubscriptionClient
    {
        public bool Released { get; private set; }
        public bool FailRelease { get; set; }

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            Task.FromResult(new SubscribeResult(Guid.NewGuid(), 0, snapshot));

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            if (FailRelease) throw new HttpRequestException("mycelium is already gone");
            Released = true;
            return Task.CompletedTask;
        }

        public Task<AddObjectsResult> AddObjectsAsync(Guid id, SubscriptionSelector s, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task RemoveObjectsAsync(Guid id, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid id, long from, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public event Action? Reconnected { add { } remove { } }
    }

    private static SnapshotThing Thing(Guid id, string name, params (string Key, object Value)[] properties) =>
        new(id, name, false,
            properties.ToDictionary(p => p.Key, p => new SnapshotProperty(
                JsonDocument.Parse(JsonSerializer.Serialize(p.Value)).RootElement, null, null)),
            new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []);

    // 24 ha split 40/60 between a built category and a productive one.
    private static SnapshotDocument WillowBend(bool categoriseGrowing = true)
    {
        var studies = Guid.NewGuid();
        var has = Guid.NewGuid();
        var categorizedAs = Guid.NewGuid();
        var residential = Guid.NewGuid();
        var food = Guid.NewGuid();

        var things = new List<SnapshotThing>
        {
            Thing(Study, "study"), Thing(Site, "WillowBend"),
            Thing(studies, "studies"), Thing(has, "has"),
            Thing(categorizedAs, "categorizedAs", (ProgrammeSplitReader.CategoryFlag, true)),
            Thing(residential, "residential", (ProgrammeSplitReader.BuiltFootprintFlag, true)),
            Thing(food, "food-and-agriculture", (ProgrammeSplitReader.ProductiveFootprintFlag, true)),
            Thing(Parcel, "parcel", (ProgrammeSplitReader.ParcelAreaProperty, 24.0)),
            Thing(Housing, "housing", (ProgrammeSplitReader.SharePctProperty, 40.0)),
            Thing(Growing, "growing", (ProgrammeSplitReader.SharePctProperty, 60.0)),
        };

        var edges = new List<SnapshotRelationship>
        {
            Edge(Study, studies, Site),
            Edge(Site, has, Parcel), Edge(Site, has, Housing), Edge(Site, has, Growing),
            Edge(Housing, categorizedAs, residential),
        };
        if (categoriseGrowing) edges.Add(Edge(Growing, categorizedAs, food));

        return new SnapshotDocument(0, things, edges);
    }

    private static (LandAllocationReactiveHandler handler, RecordingHandler http, StubSubscriptions subscriptions)
        NewHandler(SnapshotDocument snapshot, Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var http = new RecordingHandler(responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        }));
        var subscriptions = new StubSubscriptions(snapshot);
        return (new LandAllocationReactiveHandler(
            new TestHttpClientFactory(new HttpClient(http)),
            NullLogger<LandAllocationReactiveHandler>.Instance,
            "http://mycelium", "test-token", subscriptions), http, subscriptions);
    }

    [Fact]
    public async Task An_area_is_written_onto_the_allocation_it_describes_and_a_footprint_onto_the_study()
    {
        // The split belongs to the allocations; only the footprints are about the whole parcel.
        var (handler, http, _) = NewHandler(WillowBend());

        await handler.RecomputeAsync(Study);

        http.Writes.Should().Contain(write =>
            write.Uri.Contains(Housing.ToString()) && write.Uri.Contains(LandAllocationReactiveHandler.AllocatedAreaOutput));
        http.Writes.Should().Contain(write =>
            write.Uri.Contains(Study.ToString()) && write.Uri.Contains(LandAllocationReactiveHandler.BuiltFootprintOutput));
        http.Writes.Should().NotContain(write =>
            write.Uri.Contains(Study.ToString()) && write.Uri.Contains(LandAllocationReactiveHandler.AllocatedAreaOutput));
    }

    [Fact]
    public async Task The_areas_written_are_the_ones_the_split_works_out()
    {
        var (handler, http, _) = NewHandler(WillowBend());

        var outputs = await handler.RecomputeAsync(Study);

        outputs.AreaByCategory["residential"].Should().BeApproximately(9.6, 1e-9);
        outputs.BuiltFootprintHectares.Should().BeApproximately(9.6, 1e-9);
        outputs.ProductiveFootprintHectares.Should().BeApproximately(14.4, 1e-9);
        http.Writes.Single(w => w.Uri.Contains(Housing.ToString())
                                && w.Uri.Contains(LandAllocationReactiveHandler.AllocatedAreaOutput))
            .Body.Should().Contain("9.6");
    }

    [Fact]
    public async Task An_allocation_naming_no_category_fails_the_recompute_and_writes_nothing()
    {
        // Allocating the rest would describe a different parcel than the one submitted, and the study
        // would carry footprints computed from a split that is short by one.
        var (handler, http, _) = NewHandler(WillowBend(categoriseGrowing: false));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.RecomputeAsync(Study));

        refusal.Message.Should().Contain("growing");
        http.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task What_it_read_from_is_the_allocations_and_the_parcel_for_the_follower_to_watch()
    {
        var (handler, _, _) = NewHandler(WillowBend());

        await handler.RecomputeAsync(Study);

        handler.ReadsFrom.Should().BeEquivalentTo(new[] { Parcel, Housing, Growing });
    }

    [Fact]
    public async Task The_subscription_is_released_even_when_the_split_cannot_be_computed()
    {
        var (handler, _, subscriptions) = NewHandler(WillowBend(categoriseGrowing: false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.RecomputeAsync(Study));

        subscriptions.Released.Should().BeTrue("a read that fails still holds a subscription open");
    }

    [Fact]
    public async Task A_read_that_succeeded_is_not_lost_because_the_subscription_could_not_be_released()
    {
        var (handler, http, subscriptions) = NewHandler(WillowBend());
        subscriptions.FailRelease = true;

        var outputs = await handler.RecomputeAsync(Study);

        outputs.BuiltFootprintHectares.Should().BeApproximately(9.6, 1e-9);
        http.Writes.Should().NotBeEmpty("the split was read and computed before the release was attempted");
    }

    [Fact]
    public async Task A_refused_write_is_raised_rather_than_leaving_a_partial_split_on_the_model()
    {
        var (handler, _, _) = NewHandler(WillowBend(),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => handler.RecomputeAsync(Study));

        failure.Message.Should().Contain(LandAllocationReactiveHandler.AllocatedAreaOutput);
    }

    // This service is the one where writing onto what it follows is unavoidable: the area and the
    // normalised share land on the allocations it names as the Things it read from, so a change to either
    // arrives as a change on a followed Thing. The share a planner set is what may wake it; the share it
    // worked out from that is not.
    [Fact]
    public void None_of_the_outputs_it_writes_can_wake_it()
    {
        LandAllocationReactiveHandler.InputProperties.Should()
            .NotIntersectWith(DeclaredOutputs.Of<LandAllocationReactiveHandler>());
    }
}
