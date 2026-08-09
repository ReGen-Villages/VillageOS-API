using System.Text.Json;
using FluentAssertions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.Service.Tributary.Tests.Helpers;

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

    // Mycelium reports a key that several templates on one `is` chain declare once per declaring
    // template, qualified by the path from the endpoint Thing. Those are one key shadowed along a
    // chain, so the closest declaration wins; only paths that diverge are ambiguous.

    [Fact]
    public void TryGetEffectiveProperty_SingleChainTwoLevels_ClosestAncestorWins()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["EsriEndpoint.requestContentType"]          = El("\"application/x-www-form-urlencoded\""),
            ["EsriEndpoint.Endpoint.requestContentType"] = El("\"application/json\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "requestContentType", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("application/x-www-form-urlencoded");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_SingleChainThreeLevels_ClosestAncestorWins()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["A.B.C.x"] = El("\"root\""),
            ["A.x"]     = El("\"closest\""),
            ["A.B.x"]   = El("\"middle\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "x", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("closest");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_SingleChainCaseInsensitivePrefix_ClosestAncestorWins()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["esriendpoint.responseKind"]          = El("\"binary\""),
            ["EsriEndpoint.Endpoint.responseKind"] = El("\"json\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "responseKind", out var value, out var conflicts);

        found.Should().BeTrue();
        value.GetString().Should().Be("binary");
        conflicts.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveProperty_MixedChainAndDivergentBranch_StillConflicts()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["A.x"]   = El("\"a\""),
            ["A.B.x"] = El("\"ab\""),
            ["C.x"]   = El("\"c\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "x", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeEquivalentTo(new[] { "A.x", "A.B.x", "C.x" });
    }

    [Fact]
    public void TryGetEffectiveProperty_ShorterPathIsAStringPrefixButNotASegmentPrefix_StillConflicts()
    {
        // "Esri" is a string prefix of "EsriEndpoint" but names a different template, so the
        // shorter path is not an ancestor of the longer one.
        var props = new Dictionary<string, JsonElement>
        {
            ["Esri.url"]                  = El("\"https://a\""),
            ["EsriEndpoint.Endpoint.url"] = El("\"https://b\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeEquivalentTo(new[] { "Esri.url", "EsriEndpoint.Endpoint.url" });
    }

    [Fact]
    public void TryGetEffectiveProperty_PathsSharingALeadingSegmentThenDiverging_StillConflicts()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["A.B.x"]   = El("\"ab\""),
            ["A.C.D.x"] = El("\"acd\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "x", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeEquivalentTo(new[] { "A.B.x", "A.C.D.x" });
    }

    [Fact]
    public void TryGetEffectiveProperty_SameDepthDivergentPaths_StillConflicts()
    {
        var props = new Dictionary<string, JsonElement>
        {
            ["Esri.url"]         = El("\"https://a\""),
            ["EsriEndpoint.url"] = El("\"https://b\"")
        };

        var found = EffectivePropertyResolver.TryGetEffectiveProperty(props, "url", out _, out var conflicts);

        found.Should().BeFalse();
        conflicts.Should().BeEquivalentTo(new[] { "Esri.url", "EsriEndpoint.url" });
    }
}
