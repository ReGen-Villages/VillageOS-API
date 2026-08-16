using FluentAssertions;
using vos.Service.Delta.Models;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Pins the canonical EsriTileEndpoint template contract for Story #5914 (Feature #5779). The
// template hierarchy is not a committed file — deployment seed.json stores every template as a
// thing plus the is relationships between them — so this test is the executable spec for the
// tile-endpoint shape Delta is expected to resolve.
// Key design point: a map tile is a plain unauthenticated GET whose body is bytes, not text.
// "Tile-ness" is therefore pure template config: the EsriTileEndpoint template reaches the
// BinaryResponse kind through readsBodyAs and declares acceptHeader as an optional blank for
// content-negotiating upstreams (#5913). It descends from the root directly — public tile
// servers need none of EsriEndpoint's token exchange or offset paging.
public class EsriTileEndpointTemplateTests
{
    private static RegisterEndpointRequest Thing(string name, Dictionary<string, object> props) =>
        new() { Name = name, Properties = props };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    private static SeedRelationship Uses(string subject, string role, string kind) =>
        new() { Subject = subject, Predicate = role, Target = kind };

    // The canonical Endpoint -> EsriTileEndpoint hierarchy. The root carries the base-capability
    // keys as blank structural keys and reaches no kind; EsriTileEndpoint adds only what a tile
    // fetch needs: the BinaryResponse kind and an optional acceptHeader blank.
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
            }),
            Thing("EsriTileEndpoint", new()
            {
                ["acceptHeader"] = "",   // per-registration format negotiation (optional)
            }),
        },
        Kinds = new()
        {
            new EndpointKind { Name = "BinaryResponse", Requires = [] },
        },
        Relationships = new()
        {
            Is("EsriTileEndpoint", "Endpoint"),
            Uses("EsriTileEndpoint", EndpointKindRoles.ResponseBody, "BinaryResponse"),
        },
    });

    [Fact]
    public void Graph_EsriTileEndpointExtendsRoot_BuildsValidSingleRootedGraph()
    {
        var graph = Graph();

        graph.Root.Name.Should().Be("Endpoint");
        graph.ContainsTemplate("EsriTileEndpoint").Should().BeTrue();
        graph.ParentName("EsriTileEndpoint").Should().Be("Endpoint");
    }

    [Fact]
    public void ResponseBody_RootReachesNoKind_SoAPlainEndpointReadsText()
    {
        Graph().ResolveKind("Endpoint", EndpointKindRoles.ResponseBody).Should().BeNull();
    }

    [Fact]
    public void ResponseBody_ResolvedForTile_IsBinaryResponse()
    {
        Graph().ResolveKind("EsriTileEndpoint", EndpointKindRoles.ResponseBody)!
            .Name.Should().Be("BinaryResponse");
    }

    [Fact]
    public void ResponseBody_BinaryRequiresNothing_SoARegistrationOwesOnlyTheUrl()
    {
        // Reading bytes needs no configuration beyond the fetch itself; the only blank a
        // registration must fill is the root's url.
        var kind = Graph().ResolveKind("EsriTileEndpoint", EndpointKindRoles.ResponseBody)!;

        kind.Requires.Should().BeEmpty();
        Graph().TryGetEffectiveSeedValue("EsriTileEndpoint", "url", out _).Should().BeFalse();
    }

    [Fact]
    public void AuthenticationAndPaging_TileReachesNoKind_PublicServersNeedNeither()
    {
        Graph().ResolveKind("EsriTileEndpoint", EndpointKindRoles.Authentication).Should().BeNull();
        Graph().ResolveKind("EsriTileEndpoint", EndpointKindRoles.Paging).Should().BeNull();
    }

    [Fact]
    public void HttpMethod_InheritedNotRestated_IsGetForTile()
    {
        // The tile template does not redeclare httpMethod; the root's GET arrives by inheritance.
        Graph().TryGetEffectiveSeedValue("EsriTileEndpoint", "httpMethod", out var value).Should().BeTrue();
        value.Should().Be("GET");
    }

    [Fact]
    public void AllowedKeys_Tile_UnionsBaseKeysAndAcceptHeader()
    {
        Graph().AllowedKeys("EsriTileEndpoint").Should().BeEquivalentTo(new[]
        {
            // inherited from the root
            "url", "httpMethod", "responseTransform", "headers", "queryParams",
            "requestContentType", "timeout",
            // declared on EsriTileEndpoint
            "acceptHeader",
        });
    }

    [Fact]
    public void SupersededResponseKindProperty_OnTheTileTemplate_IsRefusedNamingTheEdge()
    {
        // The pre-kinds spelling of exactly this template — a responseKind property — must be
        // refused at build, pointing at readsBodyAs instead.
        var act = () => EndpointSeedGraph.Build(new EndpointSeedModel
        {
            Things = new()
            {
                Thing("Endpoint", new() { ["url"] = "" }),
                Thing("EsriTileEndpoint", new() { ["responseKind"] = "binary" }),
            },
            Relationships = new() { Is("EsriTileEndpoint", "Endpoint") },
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EsriTileEndpoint*responseKind*readsBodyAs*");
    }
}
