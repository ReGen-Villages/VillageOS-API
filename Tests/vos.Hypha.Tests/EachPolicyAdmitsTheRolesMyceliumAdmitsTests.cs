using FluentAssertions;
using vos.Hypha.Registry;
using Xunit;

namespace vos.Hypha.Tests;

public class EachPolicyAdmitsTheRolesMyceliumAdmitsTests
{
    private static readonly string[] Roles = ["viewer", "editor", "admin", "service", "someone"];

    [Theory]
    [InlineData(MyceliumPolicy.ReadModel, "viewer editor admin service")]
    [InlineData(MyceliumPolicy.ModifyData, "editor admin service")]
    [InlineData(MyceliumPolicy.WritePropertyFact, "editor admin service")]
    [InlineData(MyceliumPolicy.WritePropertyObservation, "editor admin service")]
    [InlineData(MyceliumPolicy.ManageRanges, "editor admin")]
    [InlineData(MyceliumPolicy.EditorOrAdmin, "editor admin")]
    [InlineData(MyceliumPolicy.AdminOnly, "admin")]
    public void A_policy_admits_exactly_its_roles(MyceliumPolicy policy, string admitted)
    {
        Roles.Where(role => policy.IsSatisfiedBy(role)).Should().Equal(admitted.Split(' '));
    }
}
