using FluentAssertions;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.Service.Shared.Tests.Roles;

// The identifiers are written out beside their words so a reader of the file sees both, and this holds the two
// together: a role whose identifier is not the standard version 5 identifier of its word would select a Thing no
// template declares, and every reader of that role would find the model binding nothing.
public class LandIntakeRolesTests
{
    public static IEnumerable<object[]> EveryRole() => LandIntakeRoles.All.Select(role => new object[] { role });

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void A_role_is_identified_by_the_version_5_identifier_of_its_word(Role role)
    {
        role.Id.Should().Be(RoleIdentity.Of(role.Word), $"the role {role.Word}");
    }

    [Fact]
    public void No_two_roles_share_a_word_or_an_identifier()
    {
        LandIntakeRoles.All.Select(role => role.Word).Should().OnlyHaveUniqueItems();
        LandIntakeRoles.All.Select(role => role.Id).Should().OnlyHaveUniqueItems();
    }

    // The identifiers the platform's templates declare, copied from land-intake.template.json and
    // site-analysis.template.json, so a derivation that drifted from the platform's would be caught here.
    [Theory]
    [InlineData("siteStudy", "e9036a23-21c1-5956-b0d8-a212e8ad8272")]
    [InlineData("proposedSite", "ac8f6596-7dd4-53bb-bcc9-01d3f7b78b5b")]
    [InlineData("placeNesting", "c3c9edde-b271-5a52-8f23-cc6bae9ea1d1")]
    public void The_derivation_agrees_with_the_identifiers_the_templates_declare(string word, string declared)
    {
        RoleIdentity.Of(word).Should().Be(Guid.Parse(declared));
    }
}
