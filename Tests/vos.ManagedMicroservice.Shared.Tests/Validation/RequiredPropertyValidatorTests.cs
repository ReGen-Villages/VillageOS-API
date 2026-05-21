using FluentAssertions;
using vos.ManagedMicroservice.Shared.Validation;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests.Validation;

public class RequiredPropertyValidatorTests
{
    [Fact]
    public void GetMissingRequiredKeys_NullRequired_ReturnsEmpty()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: null,
            providedKeys: new[] { "anything" });

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMissingRequiredKeys_AllProvided_ReturnsEmpty()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: new[] { "url", "method" },
            providedKeys: new[] { "url", "method", "extra" });

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMissingRequiredKeys_SomeMissing_ReturnsThemInRequiredOrder()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: new[] { "a", "b", "c", "d" },
            providedKeys: new[] { "c" });

        result.Should().Equal("a", "b", "d");
    }

    [Fact]
    public void GetMissingRequiredKeys_NullProvided_AllRequiredAreMissing()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: new[] { "x", "y" },
            providedKeys: null);

        result.Should().Equal("x", "y");
    }

    [Fact]
    public void GetMissingRequiredKeys_DefaultComparerIsCaseSensitive()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: new[] { "Url" },
            providedKeys: new[] { "url" });

        result.Should().Equal("Url");
    }

    [Fact]
    public void GetMissingRequiredKeys_HonorsCustomComparer()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: new[] { "Url", "Method" },
            providedKeys: new[] { "url", "method" },
            comparer: StringComparer.OrdinalIgnoreCase);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMissingRequiredKeys_EmptyRequired_ReturnsEmpty()
    {
        var result = RequiredPropertyValidator.GetMissingRequiredKeys(
            requiredKeys: Array.Empty<string>(),
            providedKeys: new[] { "url" });

        result.Should().BeEmpty();
    }
}
