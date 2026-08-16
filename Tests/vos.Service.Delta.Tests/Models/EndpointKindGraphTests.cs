using FluentAssertions;
using vos.Service.Delta.Models;
using Xunit;

namespace vos.Service.Delta.Tests.Models;

// A kind is a Thing an endpoint relates to, declaring what it requires of the endpoints that use
// it. These pin the three things the graph has to get right: a kind is not a template and must not
// count as a second root, a template resolves the kind its chain reaches, and a seed still naming a
// kind in a property is refused rather than provisioned half-right.
public class EndpointKindGraphTests
{
    private static EndpointSeedModel SeedWithKind() => new()
    {
        Things =
        [
            new RegisterEndpointRequest { Name = "Endpoint", Properties = new() { ["url"] = "", ["httpMethod"] = "" } },
            new RegisterEndpointRequest { Name = "EsriEndpoint", Properties = new() { ["tokenUrl"] = "https://example.test/token" } },
        ],
        Kinds =
        [
            new EndpointKind { Name = "TokenExchangeAuth", Requires = ["tokenUrl", "tokenRequest", "tokenPath"] },
        ],
        Relationships =
        [
            new SeedRelationship { Subject = "EsriEndpoint", Predicate = "is", Target = "Endpoint" },
            new SeedRelationship { Subject = "EsriEndpoint", Predicate = "authenticatesBy", Target = "TokenExchangeAuth" },
        ],
    };

    [Fact]
    public void Build_KindAlongsideTemplates_DoesNotCountAsASecondRoot()
    {
        var graph = EndpointSeedGraph.Build(SeedWithKind());

        graph.Root.Name.Should().Be("Endpoint");
        graph.Templates.Should().NotContainKey("TokenExchangeAuth");
        graph.Kinds.Should().ContainKey("TokenExchangeAuth");
    }

    [Fact]
    public void ResolveKind_TemplateDeclaringTheEdge_ReachesItsKind()
    {
        var graph = EndpointSeedGraph.Build(SeedWithKind());

        graph.ResolveKind("EsriEndpoint", "authenticatesBy")!.Name.Should().Be("TokenExchangeAuth");
    }

    [Fact]
    public void ResolveKind_TemplateWhoseAncestorDeclaresTheEdge_InheritsIt()
    {
        var seed = SeedWithKind();
        seed.Things.Add(new RegisterEndpointRequest { Name = "EsriFeatureLayer" });
        seed.Relationships.Add(new SeedRelationship
        {
            Subject = "EsriFeatureLayer", Predicate = "is", Target = "EsriEndpoint",
        });

        EndpointSeedGraph.Build(seed).ResolveKind("EsriFeatureLayer", "authenticatesBy")!
            .Name.Should().Be("TokenExchangeAuth");
    }

    [Fact]
    public void ResolveKind_NoEdgeAnywhereOnTheChain_ResolvesToNothing()
    {
        EndpointSeedGraph.Build(SeedWithKind()).ResolveKind("Endpoint", "authenticatesBy").Should().BeNull();
    }

    [Fact]
    public void Build_TemplateNamingItsKindInAProperty_IsRefusedNamingTheEdgeToWriteInstead()
    {
        var seed = SeedWithKind();
        seed.Things[1].Properties!["authKind"] = "tokenExchange";

        var refusal = Assert.Throws<InvalidOperationException>(() => EndpointSeedGraph.Build(seed));

        refusal.Message.Should().Contain("EsriEndpoint");
        refusal.Message.Should().Contain("authKind");
        refusal.Message.Should().Contain("authenticatesBy");
    }

    [Fact]
    public void Build_TemplateDeclaringTwoKindsOfOneRole_IsRefused()
    {
        var seed = SeedWithKind();
        seed.Kinds.Add(new EndpointKind { Name = "NoAuth" });
        seed.Relationships.Add(new SeedRelationship
        {
            Subject = "EsriEndpoint", Predicate = "authenticatesBy", Target = "NoAuth",
        });

        Assert.Throws<InvalidOperationException>(() => EndpointSeedGraph.Build(seed))
            .Message.Should().Contain("EsriEndpoint");
    }

    [Fact]
    public void Build_EdgeToAKindThatDoesNotExist_IsRefused()
    {
        var seed = SeedWithKind();
        seed.Relationships.Add(new SeedRelationship
        {
            Subject = "Endpoint", Predicate = "pagesBy", Target = "OffsetPaging",
        });

        Assert.Throws<InvalidOperationException>(() => EndpointSeedGraph.Build(seed))
            .Message.Should().Contain("OffsetPaging");
    }
}
