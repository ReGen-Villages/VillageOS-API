using FluentAssertions;
using vos.Service.Delta.Models;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Pins the canonical EsriEndpoint template contract for Task #5470. The template hierarchy is not a
// committed file — deployment seed.json stores every template as a thing plus the is
// relationships between them — so this test is the executable spec for the EsriEndpoint shape Delta
// is expected to resolve.
// Key design point: Tributary's handling is source-agnostic (a generic token-exchange + offset
// paginator). "ESRI-ness" is therefore pure template config: the EsriEndpoint template reaches the
// TokenExchangeAuth and OffsetPaging kinds and supplies the ArcGIS field names (tokenPath=token,
// hasMorePath=exceededTransferLimit, itemsPath=features, …). A kind is a Thing the template
// relates to, never a word it carries, so what a kind requires can be read before anything is called.
public class EsriEndpointTemplateTests
{
    private static RegisterEndpointRequest Thing(string name, Dictionary<string, object> props) =>
        new() { Name = name, Properties = props };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    private static SeedRelationship Uses(string subject, string role, string kind) =>
        new() { Subject = subject, Predicate = role, Target = kind };

    // The canonical Endpoint -> EsriEndpoint hierarchy. The root carries the base-capability keys
    // (Task #5469) as blank structural keys and reaches no kind; EsriEndpoint restates the narrowed
    // keys, reaches its two kinds, and sets the generic token-exchange / offset-paging keys to the
    // ArcGIS field names.
    private static EndpointSeedGraph Graph() => EndpointSeedGraph.Build(new EndpointSeedModel
    {
        Things = new()
        {
            Thing("Endpoint", new()
            {
                ["url"] = "",
                ["httpMethod"] = "GET",
                ["responseTransform"] = "",
                ["headers"] = "",
                ["queryParams"] = "",
                ["requestContentType"] = "",
                ["timeout"] = "",
            }),
            Thing("EsriEndpoint", new()
            {
                // narrowed base keys
                ["httpMethod"] = "POST",
                ["requestContentType"] = "application/x-www-form-urlencoded",
                // auth: a token-exchange whose ArcGIS specifics are config
                ["token"] = "",          // per-registration pre-minted token (optional)
                ["tokenUrl"] = "",       // per-registration token endpoint
                ["tokenRequest"] = "",   // per-registration credential form fields
                ["tokenPath"] = "token",
                ["expiryPath"] = "expires",
                ["expiryUnit"] = "epochMillis",
                // paging: offset window over a FeatureServer query
                ["offsetParam"] = "resultOffset",
                ["pageSizeParam"] = "resultRecordCount",
                ["hasMorePath"] = "exceededTransferLimit",
                ["itemsPath"] = "features",
                ["pageSize"] = "",       // per-registration page size (optional)
            }),
        },
        Kinds = new()
        {
            new EndpointKind { Name = "TokenExchangeAuth", Requires = ["tokenUrl", "tokenRequest", "tokenPath"] },
            new EndpointKind { Name = "OffsetPaging", Requires = ["offsetParam", "hasMorePath", "itemsPath"] },
        },
        Relationships = new()
        {
            Is("EsriEndpoint", "Endpoint"),
            Uses("EsriEndpoint", EndpointKindRoles.Authentication, "TokenExchangeAuth"),
            Uses("EsriEndpoint", EndpointKindRoles.Paging, "OffsetPaging"),
        },
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
    public void Authentication_RootReachesNoKind_SoAPlainEndpointAuthenticatesWithNothing()
    {
        Graph().ResolveKind("Endpoint", EndpointKindRoles.Authentication).Should().BeNull();
    }

    [Fact]
    public void Authentication_ResolvedByChild_IsTokenExchangeForEsriEndpoint()
    {
        Graph().ResolveKind("EsriEndpoint", EndpointKindRoles.Authentication)!
            .Name.Should().Be("TokenExchangeAuth");
    }

    [Fact]
    public void Paging_ResolvedByChild_IsOffsetForEsriEndpoint()
    {
        Graph().ResolveKind("EsriEndpoint", EndpointKindRoles.Paging)!.Name.Should().Be("OffsetPaging");
    }

    [Fact]
    public void Paging_TheTemplateSuppliesEveryFieldOffsetPagingRequires()
    {
        // Paging is fully described by the template, so nothing is left for a registration to supply.
        var kind = Graph().ResolveKind("EsriEndpoint", EndpointKindRoles.Paging)!;
        Graph().AllowedKeys("EsriEndpoint").Should().Contain(kind.Requires);
    }

    [Fact]
    public void Authentication_CredentialsAreLeftToTheRegistration_AndAreReportedAsOutstanding()
    {
        // tokenUrl and tokenRequest are blank structural keys: an ArcGIS server's address and its
        // credentials belong to whoever registers the endpoint, not to the template they share. The
        // kind names them so a registration missing them is refused before any call is attempted.
        var kind = Graph().ResolveKind("EsriEndpoint", EndpointKindRoles.Authentication)!;
        kind.Requires.Where(required =>
                !Graph().TryGetEffectiveSeedValue("EsriEndpoint", required, out _))
            .Should().BeEquivalentTo("tokenUrl", "tokenRequest");
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
            "requestContentType", "timeout",
            // declared on EsriEndpoint
            "token", "tokenUrl", "tokenRequest", "tokenPath", "expiryPath", "expiryUnit",
            "offsetParam", "pageSizeParam", "hasMorePath", "itemsPath", "pageSize",
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
