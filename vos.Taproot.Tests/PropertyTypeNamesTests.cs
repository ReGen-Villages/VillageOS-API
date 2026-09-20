using FluentAssertions;
using Xunit;

namespace vos.Taproot.Tests;

public class PropertyTypeNamesTests
{
    [Theory]
    [InlineData("string", "vos.String")]
    [InlineData("STRING", "vos.String")]
    [InlineData("System.String", "vos.String")]
    [InlineData("int", "vos.Integer")]
    [InlineData("System.Int32", "vos.Integer")]
    [InlineData("long", "vos.LongInteger")]
    [InlineData("double", "vos.Double")]
    [InlineData("float", "vos.Float")]
    [InlineData("decimal", "vos.Decimal")]
    [InlineData("bool", "vos.Boolean")]
    [InlineData("datetime", "vos.DateTime")]
    [InlineData("guid", "vos.Guid")]
    public void Canonical_MapsTheShortAndClrNamesTheGuideListsOntoThePlatforms(string typed, string expected)
    {
        PropertyTypeNames.Canonical(typed).Should().Be(expected);
    }

    [Theory]
    [InlineData("vos.Integer")]
    [InlineData("vos.GeoJson")]
    [InlineData("banana")]
    public void Canonical_PassesThePlatformsOwnNamesAndAnythingElseThroughAsTyped(string typed)
    {
        PropertyTypeNames.Canonical(typed).Should().Be(typed);
    }
}
