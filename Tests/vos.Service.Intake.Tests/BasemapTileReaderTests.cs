using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class BasemapTileReaderTests
{
    // The registration is reached by no relationship, so the read asks for the role's bound Thing alone and for
    // the role Thing itself with its binding, or nothing says which Thing is the registration.
    [Fact]
    public void The_read_asks_for_the_registration_role_and_what_it_binds()
    {
        var selector = BasemapTileReader.Selector();

        selector.Ids.Should().Equal(BasemapTileReader.RegistrationRole.Id);
        selector.BoundArchetypes.Should().Equal(BasemapTileReader.RegistrationRole.Id);
        selector.Names.Should().Equal(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings);
    }

    [Fact]
    public void The_registration_role_is_the_version_5_identifier_of_its_word()
    {
        BasemapTileReader.RegistrationRole.Id.Should().Be(RoleIdentity.Of(BasemapTileReader.RegistrationRole.Word));
    }
}
