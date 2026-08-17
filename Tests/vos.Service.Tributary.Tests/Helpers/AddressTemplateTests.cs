using FluentAssertions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.Service.Tributary.Tests.Helpers;

public class AddressTemplateTests
{
    private static Dictionary<string, string> Values(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Fill_SubstitutesEveryPlaceholder()
    {
        var filled = AddressTemplate.Fill(
            "https://tiles.test/tile/{z}/{y}/{x}.png",
            Values(("z", "9"), ("y", "271"), ("x", "301")),
            out var missing);

        filled.Should().Be("https://tiles.test/tile/9/271/301.png");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Fill_SubstitutesInTheQueryStringAsWellAsThePath()
    {
        var filled = AddressTemplate.Fill(
            "https://api.test/{version}/forecast?latitude={lat}&longitude={lng}",
            Values(("version", "v1"), ("lat", "-25.75"), ("lng", "28.19")),
            out _);

        filled.Should().Be("https://api.test/v1/forecast?latitude=-25.75&longitude=28.19");
    }

    [Fact]
    public void Fill_RepeatedPlaceholder_SubstitutesEveryOccurrence()
    {
        var filled = AddressTemplate.Fill(
            "https://api.test/{site}/summary?for={site}",
            Values(("site", "willow")),
            out _);

        filled.Should().Be("https://api.test/willow/summary?for=willow");
    }

    [Fact]
    public void Fill_NamesEveryUnfilledPlaceholder_NotOnlyTheFirst()
    {
        AddressTemplate.Fill(
            "https://tiles.test/tile/{z}/{y}/{x}.png",
            Values(("y", "271")),
            out var missing);

        missing.Should().BeEquivalentTo(new[] { "z", "x" });
    }

    [Fact]
    public void Fill_UnfilledPlaceholder_LeavesTheTemplateUnchanged()
    {
        // The caller refuses on `missing`; leaving the text alone keeps a half-filled address from
        // ever being mistaken for a real one.
        var filled = AddressTemplate.Fill("https://tiles.test/{z}/{y}", Values(("z", "9")), out var missing);

        missing.Should().NotBeEmpty();
        filled.Should().Be("https://tiles.test/{z}/{y}");
    }

    [Fact]
    public void Fill_ValueNoPlaceholderNames_IsIgnored()
    {
        // One caller passes a shared set of values to sources whose addresses take different
        // placeholders, so an unused value is ordinary rather than a mistake.
        var filled = AddressTemplate.Fill(
            "https://api.test/forecast?latitude={lat}",
            Values(("lat", "-25.75"), ("lng", "28.19"), ("elevation", "1200")),
            out var missing);

        filled.Should().Be("https://api.test/forecast?latitude=-25.75");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Fill_NoPlaceholders_ReturnsTheAddressUnchanged()
    {
        var filled = AddressTemplate.Fill(
            "https://api.test/fixed?f=json",
            Values(("lat", "-25.75")),
            out var missing);

        filled.Should().Be("https://api.test/fixed?f=json");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Fill_NoValuesSupplied_ReportsThePlaceholdersAsMissing()
    {
        AddressTemplate.Fill("https://tiles.test/{z}", null, out var missing);

        missing.Should().BeEquivalentTo(new[] { "z" });
    }

    [Theory]
    [InlineData("a&b=c", "a%26b%3Dc")]
    [InlineData("../etc", "..%2Fetc")]
    [InlineData("with space", "with%20space")]
    [InlineData("100%", "100%25")]
    public void Fill_EscapesTheValue_SoItCannotAlterTheAddressStructure(string value, string expected)
    {
        var filled = AddressTemplate.Fill("https://api.test/lookup?name={name}", Values(("name", value)), out _);

        filled.Should().Be($"https://api.test/lookup?name={expected}");
    }

    [Fact]
    public void Fill_PlaceholderNameMatchIsCaseInsensitive()
    {
        var filled = AddressTemplate.Fill("https://api.test/{Site}", Values(("site", "willow")), out var missing);

        filled.Should().Be("https://api.test/willow");
        missing.Should().BeEmpty();
    }
}
