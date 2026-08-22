using FluentAssertions;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.Service.Tributary.Tests;

// What the ingest has to know before it relates an endpoint to a Thing: which Things this endpoint is
// already recorded as having observed, and which predicate it related through. Both come out of the
// snapshot the call already takes for the endpoint's kinds.
public class ObservedEdgesTests
{
    private static readonly Guid Endpoint = Guid.NewGuid();
    private static readonly Guid Observed = Guid.NewGuid();
    private static readonly Guid Covers = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();

    private static SnapshotThing Thing(Guid id, string name) =>
        new(id, name, false,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []);

    private static SnapshotDocument Snapshot(
        IEnumerable<SnapshotThing> things, IEnumerable<SnapshotRelationship> edges) =>
        new(0, [.. things], [.. edges]);

    [Fact]
    public void SelectorFor_AsksForTheObservedPredicateByName()
    {
        // Left out, every edge the endpoint carries reads as some other predicate and a second run
        // writes a parallel edge. Same trap the kind roles hit, for the same reason.
        var selector = EndpointKindResolver.SelectorFor(Endpoint);

        selector.Names.Should().Contain(ObservedEdges.PredicateName);
    }

    [Fact]
    public void Resolve_EdgeAlreadyWritten_NamesTheThingAndThePredicateItWentThrough()
    {
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Site, "WillowBend")],
            [Edge(Endpoint, Observed, Site)]);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().Equal(Site);
        edges.PredicateId.Should().Be(Observed);
    }

    [Fact]
    public void Resolve_EdgeUnderAnotherPredicate_IsNotProvenance()
    {
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Covers, "covers"), Thing(Site, "WillowBend")],
            [Edge(Endpoint, Covers, Site)]);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AnotherEndpointObservedTheSite_IsNotThisEndpointsEdge()
    {
        // Two registrations writing onto one Site is the ordinary case, and each owes its own edge.
        // Reading the Site's incoming edges rather than this endpoint's outgoing ones would leave the
        // second registration silently unrecorded.
        var otherEndpoint = Guid.NewGuid();
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(otherEndpoint, "Other"), Thing(Observed, ObservedEdges.PredicateName), Thing(Site, "WillowBend")],
            [Edge(otherEndpoint, Observed, Site)]);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoEdgeYet_StillNamesThePredicateTheModelHolds()
    {
        // Saves the ingest a lookup on the first write, which is every write onto a site a source has
        // not been asked about before.
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName)],
            []);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().Be(Observed);
        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_TwoThingsShareThePredicateName_LeavesTheChoiceToTheIngest()
    {
        // Names are not unique. Picking one here would relate through whichever the snapshot happened
        // to list first, so a null answer sends the ingest to resolve it the way it always has.
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Guid.NewGuid(), ObservedEdges.PredicateName)],
            []);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull();
    }

    [Fact]
    public void Resolve_ModelHoldsNoObservedThing_AnswersNone()
    {
        var snapshot = Snapshot([Thing(Endpoint, "EP")], []);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull();
        edges.ObservedThingIds.Should().BeEmpty();
    }
}
