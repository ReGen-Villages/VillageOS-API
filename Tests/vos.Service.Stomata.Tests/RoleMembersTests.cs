using FluentAssertions;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

public class RoleMembersTests
{
    private sealed class Snapshot
    {
        private readonly List<SnapshotThing> _things = [];
        private readonly List<SnapshotRelationship> _edges = [];
        private readonly Guid _is = Guid.NewGuid();
        private readonly Guid _binds = Guid.NewGuid();

        public Snapshot()
        {
            Thing(_is, RoleMembers.IsPredicateName);
            Thing(_binds, RoleBindings.BindsPredicateName);
        }

        public Guid Bound(Role role, string name)
        {
            var kind = Kind(name);
            Thing(role.Id, role.Word);
            Relate(role.Id, _binds, kind);
            return kind;
        }

        public Guid Kind(string name, params Guid[] above)
        {
            var kind = Guid.NewGuid();
            Thing(kind, name, isArchetype: true);
            foreach (var parent in above) Relate(kind, _is, parent);
            return kind;
        }

        public Guid Member(string name, params Guid[] kinds)
        {
            var member = Guid.NewGuid();
            Thing(member, name);
            foreach (var kind in kinds) Relate(member, _is, kind);
            return member;
        }

        public SnapshotDocument Document() => new(1, _things, _edges);

        private void Thing(Guid id, string name, bool isArchetype = false) =>
            _things.Add(new SnapshotThing(id, name, isArchetype, [], null, [], null));

        private void Relate(Guid subject, Guid predicate, Guid target) =>
            _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, subject, predicate, target, [], null, []));
    }

    [Fact]
    public void A_member_beneath_a_narrower_kind_is_a_member_of_the_kind_the_role_binds()
    {
        var snapshot = new Snapshot();
        var groupKind = snapshot.Bound(StomataRoles.DesignStrategyGroup, "DesignStrategyGroup");
        var comfortable = snapshot.Kind("ComfortableWithoutASystem", groupKind);
        var cooledByAirMovement = snapshot.Kind("CooledByAirMovement", comfortable);
        var direct = snapshot.Member(DesignStrategy.CoolingSystemNeeded, groupKind);
        var oneKindBelow = snapshot.Member(DesignStrategy.ComfortableAsItIs, comfortable);
        var twoKindsBelow = snapshot.Member(DesignStrategy.WithAirMovement, comfortable, cooledByAirMovement);

        var members = RoleMembers.Of(snapshot.Document(), StomataRoles.DesignStrategyGroup);

        members.Select(member => member.Id).Should().BeEquivalentTo([direct, oneKindBelow, twoKindsBelow]);
    }

    [Fact]
    public void The_kinds_in_between_and_a_kind_beside_the_bound_one_are_not_members()
    {
        var snapshot = new Snapshot();
        var groupKind = snapshot.Bound(StomataRoles.DesignStrategyGroup, "DesignStrategyGroup");
        snapshot.Kind("NeedsHeating", groupKind);
        var beside = snapshot.Kind("WindDirectionSector");
        snapshot.Member("North", beside);

        RoleMembers.Of(snapshot.Document(), StomataRoles.DesignStrategyGroup).Should().BeEmpty();
    }
}
