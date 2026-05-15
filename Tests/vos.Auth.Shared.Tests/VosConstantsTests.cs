using FluentAssertions;
using vos.Auth.Shared;
using Xunit;

namespace vos.Auth.Shared.Tests;

// Pins the public string constants. Anything that mints, validates, or checks
// these strings on the broker side keys off the same values — a rename here
// silently breaks the contract, so these tests force the rename to be an
// explicit, reviewed change.
public class VosConstantsTests
{
    [Fact]
    public void VosClaims_ConstantsHaveExpectedValues()
    {
        VosClaims.Scope.Should().Be("vos:scope");
        VosClaims.ApiKeyId.Should().Be("vos:api_key_id");
        VosClaims.TokenType.Should().Be("vos:token_type");
        VosClaims.ModelId.Should().Be("vos:model_id");
    }

    [Fact]
    public void VosRoles_ConstantsHaveExpectedValues()
    {
        VosRoles.Admin.Should().Be("admin");
        VosRoles.Editor.Should().Be("editor");
        VosRoles.Viewer.Should().Be("viewer");
        VosRoles.Service.Should().Be("service");
    }

    [Fact]
    public void VosPolicies_ConstantsHaveExpectedValues()
    {
        VosPolicies.ReadModel.Should().Be("ReadModel");
        VosPolicies.ModifyData.Should().Be("ModifyData");
        VosPolicies.ManageRanges.Should().Be("ManageRanges");
        VosPolicies.AdminOnly.Should().Be("AdminOnly");
        VosPolicies.ServiceOrAdmin.Should().Be("ServiceOrAdmin");
    }
}
