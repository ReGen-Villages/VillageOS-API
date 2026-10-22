using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class PositionLookupReaderTests
{
    // The two registrations are reached by no relationship, so the read asks for each role's bound Thing alone
    // and for the role Thing itself with its binding, or nothing says which registration is which.
    [Fact]
    public void The_read_asks_for_both_lookup_roles_and_what_each_binds()
    {
        var selector = PositionLookupReader.Selector();

        selector.Ids.Should().Equal(PositionLookupReader.ParcelLookupRole.Id, PositionLookupReader.PlaceSearchRole.Id);
        selector.BoundArchetypes.Should().Equal(PositionLookupReader.ParcelLookupRole.Id, PositionLookupReader.PlaceSearchRole.Id);
        selector.BoundTypes.Should().BeNull();
        selector.Names.Should().Equal(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings);
        selector.MarkedArchetypes.Should().BeNull();
    }

    [Fact]
    public void A_model_binding_neither_role_offers_no_lookup()
    {
        var model = DeclaredModel.Seeded()
            .Unbound(PositionLookupReader.ParcelLookupRole)
            .Unbound(PositionLookupReader.PlaceSearchRole)
            .Build();

        PositionLookupReader.ParcelLookups(model).Should().BeEmpty();
        PositionLookupReader.PlaceSearch(model).Should().BeNull();
    }
}
