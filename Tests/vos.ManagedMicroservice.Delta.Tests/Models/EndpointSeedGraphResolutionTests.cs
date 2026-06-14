using FluentAssertions;
using vos.ManagedMicroservice.Delta.Models;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests.Models;

/// <summary>
/// Unit tests for the chain-resolution surface added under Task #5467:
/// <see cref="EndpointSeedGraph.ContainsTemplate"/>, <see cref="EndpointSeedGraph.Chain"/>,
/// <see cref="EndpointSeedGraph.AllowedKeys"/>, and <see cref="EndpointSeedGraph.TryGetEffectiveSeedValue"/>.
/// Structural Build validation lives in <see cref="EndpointSeedGraphTests"/>; these pin the
/// closest-ancestor-wins resolution over the in-memory seed graph that Delta uses to verify descent
/// and validate the effective httpMethod with no mycelium round-trip.
/// </summary>
public class EndpointSeedGraphResolutionTests
{
    private static RegisterEndpointRequest Thing(string name, Dictionary<string, object>? props = null) =>
        new() { Name = name, Properties = props ?? new Dictionary<string, object>() };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    private static EndpointSeedModel Model(RegisterEndpointRequest[] things, params SeedRelationship[] rels) =>
        new() { Things = things.ToList(), Relationships = rels.ToList() };

    // CountyParcels is EsriEndpoint is Endpoint(root), with properties spread across the chain.
    private static EndpointSeedGraph ThreeLevelGraph() => EndpointSeedGraph.Build(Model(
        new[]
        {
            Thing("Endpoint", new() { ["url"] = "", ["httpMethod"] = "GET", ["responseTransform"] = "$" }),
            Thing("EsriEndpoint", new() { ["httpMethod"] = "POST", ["layer"] = "" }),
            Thing("CountyParcels", new() { ["layer"] = "3" }),
        },
        Is("EsriEndpoint", "Endpoint"), Is("CountyParcels", "EsriEndpoint")));

    // ---------- ContainsTemplate ----------

    [Fact]
    public void ContainsTemplate_KnownTemplate_True()
    {
        ThreeLevelGraph().ContainsTemplate("EsriEndpoint").Should().BeTrue();
    }

    [Fact]
    public void ContainsTemplate_CaseInsensitive_True()
    {
        ThreeLevelGraph().ContainsTemplate("esriendpoint").Should().BeTrue();
    }

    [Fact]
    public void ContainsTemplate_UnknownTemplate_False()
    {
        ThreeLevelGraph().ContainsTemplate("Ghost").Should().BeFalse();
    }

    // ---------- Chain ----------

    [Fact]
    public void Chain_LeafTemplate_NearestFirstToRoot()
    {
        var chain = ThreeLevelGraph().Chain("CountyParcels");

        chain.Select(t => t.Name).Should().ContainInOrder("CountyParcels", "EsriEndpoint", "Endpoint");
    }

    [Fact]
    public void Chain_Root_IsSelfOnly()
    {
        var chain = ThreeLevelGraph().Chain("Endpoint");

        chain.Select(t => t.Name).Should().Equal("Endpoint");
    }

    [Fact]
    public void Chain_UnknownTemplate_Throws()
    {
        var act = () => ThreeLevelGraph().Chain("Ghost");

        act.Should().Throw<KeyNotFoundException>();
    }

    // ---------- AllowedKeys ----------

    [Fact]
    public void AllowedKeys_UnionsKeysAlongChain()
    {
        ThreeLevelGraph().AllowedKeys("CountyParcels")
            .Should().BeEquivalentTo(new[] { "url", "httpMethod", "responseTransform", "layer" });
    }

    [Fact]
    public void AllowedKeys_IncludesStructuralBlankKeys()
    {
        // url is present on the root with a blank value — admissibility is by key, not value.
        ThreeLevelGraph().AllowedKeys("Endpoint").Should().Contain("url");
    }

    [Fact]
    public void AllowedKeys_IsCaseInsensitive()
    {
        ThreeLevelGraph().AllowedKeys("EsriEndpoint").Contains("HTTPMETHOD").Should().BeTrue();
    }

    // ---------- TryGetEffectiveSeedValue ----------

    [Fact]
    public void TryGetEffectiveSeedValue_ClosestAncestorWins()
    {
        // EsriEndpoint overrides httpMethod=POST over the root's GET.
        ThreeLevelGraph().TryGetEffectiveSeedValue("EsriEndpoint", "httpMethod", out var value)
            .Should().BeTrue();
        value.Should().Be("POST");
    }

    [Fact]
    public void TryGetEffectiveSeedValue_InheritsFromRootWhenLeafSilent()
    {
        // CountyParcels declares no httpMethod -> nearest declaring ancestor is EsriEndpoint (POST).
        ThreeLevelGraph().TryGetEffectiveSeedValue("CountyParcels", "httpMethod", out var value)
            .Should().BeTrue();
        value.Should().Be("POST");
    }

    [Fact]
    public void TryGetEffectiveSeedValue_BlankValuesAreSkipped()
    {
        // url is present everywhere only as a blank structural key -> no effective default.
        ThreeLevelGraph().TryGetEffectiveSeedValue("CountyParcels", "url", out var value)
            .Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetEffectiveSeedValue_UnknownKey_False()
    {
        ThreeLevelGraph().TryGetEffectiveSeedValue("CountyParcels", "nope", out var value)
            .Should().BeFalse();
        value.Should().BeNull();
    }
}
