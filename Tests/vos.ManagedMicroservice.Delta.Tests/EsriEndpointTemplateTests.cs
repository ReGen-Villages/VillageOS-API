using FluentAssertions;
using vos.ManagedMicroservice.Delta.Models;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Pins the canonical EsriEndpoint template contract for Task #5470. The template hierarchy is not a
/// committed file — deployment seed.json stores every template as a thing plus the <c>is</c>
/// relationships between them — so this test is the executable spec for the EsriEndpoint shape Delta
/// is expected to resolve.
///
/// Key design point: Tributary's handling is source-agnostic (a generic token-exchange + offset
/// paginator). "ESRI-ness" is therefore pure template <em>config</em>: the EsriEndpoint template
/// selects <c>authKind=tokenExchange</c> and <c>pagingKind=offset</c> and supplies the ArcGIS field
/// names (<c>tokenPath=token</c>, <c>hasMorePath=exceededTransferLimit</c>, <c>itemsPath=features</c>,
/// …). <c>authKind</c> itself is a structural key on the <em>root</em> Endpoint template; the child
/// only resolves its value.
/// </summary>
public class EsriEndpointTemplateTests
{
    private static RegisterEndpointRequest Thing(string name, Dictionary<string, object> props) =>
        new() { Name = name, Properties = props };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    // The canonical Endpoint -> EsriEndpoint hierarchy. The root carries the base-capability keys
    // (Task #5469) plus authKind as blank structural keys; EsriEndpoint restates the narrowed keys and
    // adds the generic token-exchange / offset-paging keys, set to the ArcGIS field names.
    private static EndpointSeedGraph Graph() => EndpointSeedGraph.Build(new EndpointSeedModel
    {
        Things = new()
        {
            Thing("Endpoint", new()
            {
                ["url"] = "",
                ["httpMethod"] = "GET",
                ["responseTransform"] = "$",
                ["headers"] = "",
                ["queryParams"] = "",
                ["requestContentType"] = "",
                ["timeout"] = "",
                ["authKind"] = "",
            }),
            Thing("EsriEndpoint", new()
            {
                // narrowed base keys
                ["httpMethod"] = "POST",
                ["requestContentType"] = "application/x-www-form-urlencoded",
                // auth: a token-exchange whose ArcGIS specifics are config
                ["authKind"] = "tokenExchange",
                ["token"] = "",          // per-registration pre-minted token (optional)
                ["tokenUrl"] = "",       // per-registration token endpoint
                ["tokenRequest"] = "",   // per-registration credential form fields
                ["tokenPath"] = "token",
                ["expiryPath"] = "expires",
                ["expiryUnit"] = "epochMillis",
                // paging: offset window over a FeatureServer query
                ["pagingKind"] = "offset",
                ["offsetParam"] = "resultOffset",
                ["pageSizeParam"] = "resultRecordCount",
                ["hasMorePath"] = "exceededTransferLimit",
                ["itemsPath"] = "features",
                ["pageSize"] = "",       // per-registration page size (optional)
            }),
        },
        Relationships = new() { Is("EsriEndpoint", "Endpoint") },
    });

    [Fact]
    public void Graph_EsriEndpointExtendsEndpoint_BuildsValidSingleRootedGraph()
    {
        var graph = Graph();

        graph.Root.Name.Should().Be("Endpoint");
        graph.ContainsTemplate("EsriEndpoint").Should().BeTrue();
        graph.ParentName("EsriEndpoint").Should().Be("Endpoint");
    }

    [Fact]
    public void AuthKind_DeclaredOnRoot_IsAllowedForEveryEndpoint()
    {
        var graph = Graph();

        // authKind is a key on the root, so it is admissible for a plain Endpoint registration too.
        graph.AllowedKeys("Endpoint").Should().Contain("authKind");
        graph.AllowedKeys("EsriEndpoint").Should().Contain("authKind");
    }

    [Fact]
    public void AuthKind_BlankOnRoot_HasNoInheritedDefault()
    {
        // A plain Endpoint leaves authKind blank -> resolves to nothing -> handler treats as "none".
        Graph().TryGetEffectiveSeedValue("Endpoint", "authKind", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void AuthKind_ResolvedByChild_IsTokenExchangeForEsriEndpoint()
    {
        Graph().TryGetEffectiveSeedValue("EsriEndpoint", "authKind", out var value).Should().BeTrue();
        value.Should().Be("tokenExchange");
    }

    [Fact]
    public void HttpMethod_NarrowedByChild_IsPostForEsriEndpoint()
    {
        // EsriEndpoint restates httpMethod=POST over the root's GET (closest-ancestor-wins).
        Graph().TryGetEffectiveSeedValue("EsriEndpoint", "httpMethod", out var value).Should().BeTrue();
        value.Should().Be("POST");
    }

    [Fact]
    public void RequestContentType_NarrowedByChild_IsFormEncodedForEsriEndpoint()
    {
        Graph().TryGetEffectiveSeedValue("EsriEndpoint", "requestContentType", out var value).Should().BeTrue();
        value.Should().Be("application/x-www-form-urlencoded");
    }

    [Theory]
    [InlineData("pagingKind", "offset")]
    [InlineData("offsetParam", "resultOffset")]
    [InlineData("hasMorePath", "exceededTransferLimit")]
    [InlineData("itemsPath", "features")]
    [InlineData("tokenPath", "token")]
    [InlineData("expiryUnit", "epochMillis")]
    public void EsriFieldNames_LiveInTemplateConfig(string key, string expected)
    {
        // The ArcGIS specifics are template data feeding the generic Tributary capabilities — there is
        // no ESRI vocabulary in code.
        Graph().TryGetEffectiveSeedValue("EsriEndpoint", key, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void AllowedKeys_EsriEndpoint_UnionsBaseAndConfigKeys()
    {
        Graph().AllowedKeys("EsriEndpoint").Should().BeEquivalentTo(new[]
        {
            // inherited from the root
            "url", "httpMethod", "responseTransform", "headers", "queryParams",
            "requestContentType", "timeout", "authKind",
            // declared on EsriEndpoint
            "token", "tokenUrl", "tokenRequest", "tokenPath", "expiryPath", "expiryUnit",
            "pagingKind", "offsetParam", "pageSizeParam", "hasMorePath", "itemsPath", "pageSize",
        });
    }

    [Fact]
    public void Url_InheritedNotRestated_RemainsBlankStructuralKey()
    {
        // EsriEndpoint does not redeclare url; it stays a blank structural key with no inherited value.
        Graph().TryGetEffectiveSeedValue("EsriEndpoint", "url", out var value).Should().BeFalse();
        value.Should().BeNull();
    }
}
