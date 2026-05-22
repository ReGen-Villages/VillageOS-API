using System.Text.Json;
using FluentAssertions;
using vos.ManagedMicroservice.Tributary.Helpers;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests.Helpers;

public class EffectivePropertyResolverTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void TryGetEffectiveProperty_ExactKeyMatch_ReturnsTrueAndValue()
    {
        var props = new Dictionary<string, JsonElement> { ["url"] = El("\"https://x\"") };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("https://x");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_SingleSuffixMatch_ReturnsTrueAndValue()
    {
        var props = new Dictionary<string, JsonElement> { ["http.url"] = El("\"https://x\"") };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("https://x");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_SuffixMatchCaseInsensitive_ReturnsTrue()
    {
        var props = new Dictionary<string, JsonElement> { ["http.URL"] = El("\"https://x\"") };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out var value, out _);

        found.Should().BeTrue();
        value.GetString().Should().Be("https://x");
    }

    [Fact]
    public void TryGetEffectiveProperty_MultipleSuffixMatches_ReturnsFalseWithConflictList()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["http.url"]     = El("\"https://a\""),
            ["resource.url"] = El("\"https://b\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().NotBeNull();
        conflicts!.Should().BeEquivalentTo(new[] { "http.url", "resource.url" });
    }

    [Fact]
    public void TryGetEffectiveProperty_NoMatch_ReturnsFalseWithNullConflicts()
    {
        var props = new Dictionary<string, JsonElement> { ["foo.bar"] = El("1") };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_EmptyDictionary_ReturnsFalse()
    {
        var props = new Dictionary<string, JsonElement>();

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_ExactMatchPreemptsSuffixMatches()
    {
        // Both an exact match AND ambiguous suffix matches exist; the exact match wins
        // and no conflicts are surfaced.
        var props = new Dictionary<string, JsonElement>
        {
            ["url"]          = El("\"direct\""),
            ["http.url"]     = El("\"https://a\""),
            ["resource.url"] = El("\"https://b\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("direct");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_KeyWithoutDot_TreatedAsItsOwnSuffix()
    {
        // Key "name" has no dot — its "suffix" is the whole key. Should match name=name.
        var props = new Dictionary<string, JsonElement> { ["name"] = El("\"alice\"") };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "name", out var value, out _);

        found.Should().BeTrue();
        value.GetString().Should().Be("alice");
    }
}
