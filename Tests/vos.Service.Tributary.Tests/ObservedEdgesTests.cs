using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.Roles;
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
    private static readonly Guid Binds = Guid.NewGuid();
    private static readonly Guid Covers = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();

    private static SnapshotThing Thing(Guid id, string name) =>
        new(id, name, false,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []);

    private static SnapshotDocument Snapshot(
        IEnumerable<SnapshotThing> things, IEnumerable<SnapshotRelationship> edges, params Guid[] boundByTheRole) =>
        new(0,
            [.. things, Thing(LandIntakeRoles.Observed.Id, "Observed"), Thing(Binds, RoleBindings.BindsPredicateName)],
            [.. edges, .. boundByTheRole.Select(bound => Edge(LandIntakeRoles.Observed.Id, Binds, bound))]);

    [Fact]
    public void SelectorFor_AsksForTheObservedRoleAndFollowsItsBinding()
    {
        // Left out, every relationship the endpoint carries reads as some other predicate and a second run
        // writes a parallel relationship. Same trap the kind roles hit, for the same reason.
        var selector = EndpointKindResolver.SelectorFor(Endpoint);

        selector.Ids.Should().Contain(LandIntakeRoles.Observed.Id);
        selector.Names.Should().Contain(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().ContainEquivalentOf(RoleBindings.FollowBindings);
    }

    [Fact]
    public void SelectorFor_AsksForThePredicateRole_SoACreatedPredicateCanJoinTheRoot()
    {
        EndpointKindResolver.SelectorFor(Endpoint).Ids.Should().Contain(LandIntakeRoles.Predicate.Id);
    }

    // Copied from open-data-sources.template.json in the platform repository; the two agree by this literal and by
    // nothing else.
    [Fact]
    public void The_created_predicate_takes_the_identifier_the_open_data_sources_template_declares()
    {
        ObservedEdges.MintedPredicateId.Should().Be(Guid.Parse("98d642a8-4811-574d-afc9-c51fd68469bc"));
    }

    [Fact]
    public void Resolve_NamesTheRootThePredicateRoleBinds()
    {
        var root = Guid.NewGuid();
        var snapshot = WithPredicateRoot(Snapshot([Thing(Endpoint, "EP"), Thing(root, "Relation")], []), root);

        ObservedEdges.Resolve(snapshot, Endpoint).PredicateRootId.Should().Be(root);
    }

    [Fact]
    public void Resolve_ThePredicateRootIsReadWhetherOrNotTheObservedRoleBindsAnything()
    {
        var root = Guid.NewGuid();
        var snapshot = WithPredicateRoot(Snapshot([Thing(Endpoint, "EP"), Thing(root, "Predicate")], []), root);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull();
        edges.PredicateRootId.Should().Be(root, "the root is what a predicate created for an unbound role joins");
    }

    [Fact]
    public void Resolve_APredicateRoleBindingTwoRoots_NamesNeither()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var snapshot = WithPredicateRoot(WithPredicateRoot(
            Snapshot([Thing(Endpoint, "EP"), Thing(first, "Predicate"), Thing(second, "Predicate")], []), first), second);

        ObservedEdges.Resolve(snapshot, Endpoint).PredicateRootId.Should().BeNull();
    }

    [Fact]
    public void Resolve_AModelWhosePredicateRoleBindsNothing_NamesNoRoot()
    {
        var snapshot = Snapshot([Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName)], [], Observed);

        ObservedEdges.Resolve(snapshot, Endpoint).PredicateRootId.Should().BeNull();
    }

    private static SnapshotDocument WithPredicateRoot(SnapshotDocument snapshot, Guid root) => snapshot with
    {
        Things = [.. snapshot.Things, Thing(LandIntakeRoles.Predicate.Id, "Predicate")],
        Relationships = [.. snapshot.Relationships, Edge(LandIntakeRoles.Predicate.Id, Binds, root)],
    };

    [Fact]
    public void Resolve_EdgeAlreadyWritten_NamesTheThingAndThePredicateItWentThrough()
    {
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Site, "WillowBend")],
            [Edge(Endpoint, Observed, Site)],
            Observed);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().Equal(Site);
        edges.PredicateId.Should().Be(Observed);
    }

    [Fact]
    public void Resolve_ThePredicateTheRoleBindsUnderAnotherName_IsTheOneUsed()
    {
        var seenBy = Guid.NewGuid();
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(seenBy, "seenBy"), Thing(Site, "WillowBend")],
            [Edge(Endpoint, seenBy, Site)],
            seenBy);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().Be(seenBy);
        edges.ObservedThingIds.Should().Equal(Site);
    }

    [Fact]
    public void Resolve_AThingNamedObservedThatNoRoleBinds_IsNotTheProvenancePredicate()
    {
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Site, "WillowBend")],
            [Edge(Endpoint, Observed, Site)]);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull("the predicate is the one the observed role binds, whatever another Thing is called");
        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EdgeUnderAnotherPredicate_IsNotProvenance()
    {
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(Covers, "covers"), Thing(Site, "WillowBend")],
            [Edge(Endpoint, Covers, Site)],
            Observed);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AnotherEndpointObservedTheSite_IsNotThisEndpointsEdge()
    {
        // Two registrations writing onto one Site is the ordinary case, and each owes its own relationship.
        // Reading the Site's incoming relationships rather than this endpoint's outgoing ones would leave the
        // second registration silently unrecorded.
        var otherEndpoint = Guid.NewGuid();
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(otherEndpoint, "Other"), Thing(Observed, ObservedEdges.PredicateName), Thing(Site, "WillowBend")],
            [Edge(otherEndpoint, Observed, Site)],
            Observed);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoEdgeYet_StillNamesThePredicateTheModelHolds()
    {
        // Saves the ingest a write on the first observation, which is every write onto a site a source has
        // not been asked about before.
        var snapshot = Snapshot([Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName)], [], Observed);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().Be(Observed);
        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ARoleBindingTwoPredicates_LeavesTheChoiceToTheIngest()
    {
        var other = Guid.NewGuid();
        var snapshot = Snapshot(
            [Thing(Endpoint, "EP"), Thing(Observed, ObservedEdges.PredicateName), Thing(other, ObservedEdges.PredicateName)],
            [],
            Observed, other);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull();
    }

    [Fact]
    public void Resolve_ModelBindingNoObservedPredicate_AnswersNone()
    {
        var snapshot = Snapshot([Thing(Endpoint, "EP")], []);

        var edges = ObservedEdges.Resolve(snapshot, Endpoint);

        edges.PredicateId.Should().BeNull();
        edges.ObservedThingIds.Should().BeEmpty();
    }

    [Fact]
    public void The_minting_fragment_creates_the_predicate_and_its_role_and_reuses_the_models_is_and_binds()
    {
        var isPredicate = Guid.NewGuid();
        var binds = Guid.NewGuid();

        var fragment = JsonSerializer.SerializeToElement(ObservedEdges.MintingFragment(isPredicate, binds, null));

        var things = fragment.GetProperty("Things").EnumerateArray().ToList();
        things.Select(thing => thing.GetProperty("Id").GetGuid()).Should().BeEquivalentTo(
            [ObservedEdges.MintedPredicateId, LandIntakeRoles.Observed.Id, RoleIdentity.Of("role")],
            "the model's own is and binds are related through, not created again");
        var relationships = fragment.GetProperty("Relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("Subject").GetGuid(), edge.GetProperty("Predicate").GetGuid(), edge.GetProperty("Target").GetGuid()))
            .ToList();
        relationships.Should().BeEquivalentTo([
            (LandIntakeRoles.Observed.Id, binds, ObservedEdges.MintedPredicateId),
            (LandIntakeRoles.Observed.Id, isPredicate, RoleIdentity.Of("role")),
        ]);
    }

    [Fact]
    public void The_minting_fragment_puts_the_predicate_under_the_root_the_model_holds()
    {
        var isPredicate = Guid.NewGuid();
        var root = Guid.NewGuid();

        var fragment = JsonSerializer.SerializeToElement(ObservedEdges.MintingFragment(isPredicate, Guid.NewGuid(), root));

        fragment.GetProperty("Relationships").EnumerateArray()
            .Select(edge => (edge.GetProperty("Subject").GetGuid(), edge.GetProperty("Predicate").GetGuid(), edge.GetProperty("Target").GetGuid()))
            .Should().Contain((ObservedEdges.MintedPredicateId, isPredicate, root));
        fragment.GetProperty("Things").EnumerateArray().Select(thing => thing.GetProperty("Id").GetGuid())
            .Should().NotContain(root, "the root is the model's own and is related to, not declared again");
    }

    [Fact]
    public void The_minting_fragment_creates_is_and_binds_where_the_model_holds_neither()
    {
        var fragment = JsonSerializer.SerializeToElement(ObservedEdges.MintingFragment(null, null, null));

        fragment.GetProperty("Things").EnumerateArray().Select(thing => thing.GetProperty("Name").GetString())
            .Should().Contain(["is", RoleBindings.BindsPredicateName]);
    }

    [Fact]
    public void The_minting_fragment_carries_only_the_fields_a_fragment_reads()
    {
        var fragment = JsonSerializer.SerializeToElement(ObservedEdges.MintingFragment(null, null, Guid.NewGuid()));

        fragment.EnumerateObject().Select(key => key.Name).Should().BeEquivalentTo(["Name", "Things", "Relationships"]);
        fragment.GetProperty("Things").EnumerateArray().SelectMany(thing => thing.EnumerateObject().Select(key => key.Name))
            .Distinct().Should().BeSubsetOf(["Id", "Name", "IsArchetype"]);
        fragment.GetProperty("Relationships").EnumerateArray().SelectMany(edge => edge.EnumerateObject().Select(key => key.Name))
            .Distinct().Should().BeSubsetOf(["Subject", "Predicate", "Target"]);
    }
}
