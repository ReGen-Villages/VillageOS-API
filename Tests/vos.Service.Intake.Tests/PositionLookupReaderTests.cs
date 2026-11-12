using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class PositionLookupReaderTests
{
    // The registrations are reached by no relationship from a site, so the read asks for each role's bound Thing
    // and the role Thing itself, and follows the fallback order out of the bound searches along with its edges.
    [Fact]
    public void The_read_asks_for_the_lookup_roles_what_each_binds_and_the_order_they_fall_back_in()
    {
        var selector = PositionLookupReader.Selector();
        Guid[] roles = [PositionLookupReader.ParcelLookupRole.Id, PositionLookupReader.PlaceSearchRole.Id, PositionLookupReader.FallsBackToRole.Id];

        selector.Ids.Should().Equal(roles);
        selector.BoundArchetypes.Should().Equal(roles);
        selector.BoundTypes.Should().BeNull();
        selector.Names.Should().Equal(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings, PositionLookupReader.FollowFallbacks);
        selector.Relationships.Should().ContainSingle(rule => rule.PredicateRole == PositionLookupReader.FallsBackToRole.Id);
    }

    [Fact]
    public void A_model_binding_neither_role_offers_no_lookup()
    {
        var model = DeclaredModel.Seeded()
            .Unbound(PositionLookupReader.ParcelLookupRole)
            .Unbound(PositionLookupReader.PlaceSearchRole)
            .Build();

        PositionLookupReader.ParcelLookups(model).Should().BeEmpty();
        PositionLookupReader.PlaceSearches(model).Should().BeEmpty();
    }

    private const string FirstSearch = "first-place-search";

    // Not the word the platform's template uses, so a reader matching the predicate by name finds no order.
    private const string ThisModelsFallbackPredicate = "triedNext";

    private static DeclaredModel FallingBackTo(DeclaredModel model, string from, string to) =>
        model.Relate(from, ThisModelsFallbackPredicate, to);

    private static DeclaredModel WithFallbacks() =>
        DeclaredModel.Seeded()
            .With(ThisModelsFallbackPredicate)
            .Bound(PositionLookupReader.FallsBackToRole, ThisModelsFallbackPredicate)
            .Stating(FirstSearch,
                (PositionLookupReader.TransformProperty, WillowBend.PlaceSearchTransform),
                (PositionLookupReader.AttributionProperty, "© the first gazetteer"))
            .Unbound(PositionLookupReader.PlaceSearchRole)
            .Bound(PositionLookupReader.PlaceSearchRole, FirstSearch);

    [Fact]
    public void Place_searches_come_in_the_order_the_bound_search_falls_back_through()
    {
        var model = FallingBackTo(WithFallbacks(), FirstSearch, WillowBend.PlaceSearchName).Build();

        PositionLookupReader.PlaceSearches(model).Select(search => search.Name)
            .Should().Equal(FirstSearch, WillowBend.PlaceSearchName);
    }

    [Fact]
    public void A_search_with_no_reshape_is_left_out_and_the_order_carries_on_past_it()
    {
        var model = FallingBackTo(WithFallbacks().Stating(FirstSearch), FirstSearch, WillowBend.PlaceSearchName).Build();

        PositionLookupReader.PlaceSearches(model).Select(search => search.Name)
            .Should().Equal(WillowBend.PlaceSearchName);
    }

    [Fact]
    public void An_order_that_comes_back_on_itself_ends_at_the_first_search_asked_twice()
    {
        var model = FallingBackTo(FallingBackTo(WithFallbacks(), FirstSearch, WillowBend.PlaceSearchName),
            WillowBend.PlaceSearchName, FirstSearch).Build();

        PositionLookupReader.PlaceSearches(model).Select(search => search.Name)
            .Should().Equal(FirstSearch, WillowBend.PlaceSearchName);
    }
}
