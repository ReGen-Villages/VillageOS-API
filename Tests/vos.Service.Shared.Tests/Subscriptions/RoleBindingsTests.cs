using FluentAssertions;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Shared.Tests.Subscriptions;

// Reading what a role binds off a snapshot. The role Thing, the `binds` predicate Thing and the relationship
// between them all have to be in the snapshot; a reader missing any of them reads a model that binds nothing.
public class RoleBindingsTests
{
    private static readonly Role Peril = new("peril", RoleIdentity.Of("peril"));
    private static readonly Guid Binds = Guid.NewGuid();
    private static readonly Guid Hazard = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static SnapshotThing Thing(Guid id, string name) => new(id, name, false, [], null, [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target, [], null, []);

    private static SnapshotDocument Snapshot(IEnumerable<SnapshotThing> things, params SnapshotRelationship[] edges) =>
        new(0, [.. things], [.. edges]);

    private static readonly SnapshotThing[] Declared =
        [Thing(Peril.Id, "Peril"), Thing(Binds, RoleBindings.BindsPredicateName), Thing(Hazard, "Hazard"), Thing(Other, "Other")];

    [Fact]
    public void The_thing_the_role_binds_is_read_off_its_binds_relationship()
    {
        var snapshot = Snapshot(Declared, Edge(Peril.Id, Binds, Hazard));

        snapshot.Bound(Peril).Select(thing => thing.Id).Should().Equal(Hazard);
    }

    [Fact]
    public void A_role_binding_two_things_answers_with_both_for_the_caller_to_refuse()
    {
        var snapshot = Snapshot(Declared, Edge(Peril.Id, Binds, Hazard), Edge(Peril.Id, Binds, Other));

        snapshot.Bound(Peril).Select(thing => thing.Name).Should().BeEquivalentTo("Hazard", "Other");
    }

    [Fact]
    public void A_role_the_model_binds_to_nothing_answers_with_none()
    {
        Snapshot(Declared).Bound(Peril).Should().BeEmpty();
    }

    // A relationship through any other predicate is not a binding, whatever its subject.
    [Fact]
    public void A_relationship_from_the_role_through_another_predicate_is_not_a_binding()
    {
        var snapshot = Snapshot(Declared, Edge(Peril.Id, Other, Hazard));

        snapshot.Bound(Peril).Should().BeEmpty();
    }

    [Fact]
    public void A_snapshot_without_the_binds_predicate_thing_reads_as_binding_nothing()
    {
        var snapshot = Snapshot(Declared.Where(thing => thing.Id != Binds), Edge(Peril.Id, Binds, Hazard));

        snapshot.Bound(Peril).Should().BeEmpty();
    }

    [Fact]
    public void A_binding_to_a_thing_the_snapshot_does_not_carry_is_left_out()
    {
        var snapshot = Snapshot(Declared.Where(thing => thing.Id != Hazard), Edge(Peril.Id, Binds, Hazard));

        snapshot.Bound(Peril).Should().BeEmpty();
    }

    [Fact]
    public void The_identifiers_of_several_roles_are_listed_for_a_selector()
    {
        var other = new Role("other", RoleIdentity.Of("other"));

        RoleBindings.Identifiers(Peril, other).Should().Equal(Peril.Id, other.Id);
    }
}
