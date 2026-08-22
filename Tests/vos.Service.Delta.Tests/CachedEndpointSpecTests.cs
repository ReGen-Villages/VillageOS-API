using FluentAssertions;
using vos.Service.Delta.Models;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Pins the cachesBy role and the DiskCache kind in the catalog vocabulary (#5918). Caching is a
// deployment choice, not part of the canonical tile template, so this spec shows the shape a
// deployment declares when it wants one: a template reaching DiskCache through cachesBy and
// supplying the cacheTtl the kind requires.
public class CachedEndpointSpecTests
{
    private static RegisterEndpointRequest Thing(string name, Dictionary<string, object> props) =>
        new() { Name = name, Properties = props };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    private static SeedRelationship Uses(string subject, string role, string kind) =>
        new() { Subject = subject, Predicate = role, Target = kind };

    private static EndpointSeedGraph Graph() => EndpointSeedGraph.Build(new EndpointSeedModel
    {
        Things = new()
        {
            Thing("Endpoint", new() { ["url"] = "", ["httpMethod"] = "GET" }),
            Thing("CachedTileEndpoint", new() { ["cacheTtl"] = "86400" }),
        },
        Kinds = new()
        {
            new EndpointKind { Name = "DiskCache", Requires = ["cacheTtl"] },
        },
        Relationships = new()
        {
            Is("CachedTileEndpoint", "Endpoint"),
            Uses("CachedTileEndpoint", EndpointKindRoles.Caching, "DiskCache"),
        },
    });

    [Fact]
    public void CachesBy_IsARecognizedRole()
    {
        EndpointKindRoles.IsRole("cachesBy").Should().BeTrue();
        EndpointKindRoles.All.Should().Contain(EndpointKindRoles.Caching);
    }

    [Fact]
    public void Caching_ResolvedForTemplate_IsDiskCacheWithItsRequirement()
    {
        var kind = Graph().ResolveKind("CachedTileEndpoint", EndpointKindRoles.Caching);

        kind.Should().NotBeNull();
        kind!.Name.Should().Be("DiskCache");
        kind.Requires.Should().BeEquivalentTo("cacheTtl");
    }

    [Fact]
    public void Caching_RootReachesNoKind_SoEveryCallRefetchesByDefault()
    {
        Graph().ResolveKind("Endpoint", EndpointKindRoles.Caching).Should().BeNull();
    }

    [Fact]
    public void CacheTtl_SuppliedByTheTemplate_SoARegistrationOwesOnlyTheUrl()
    {
        Graph().TryGetEffectiveSeedValue("CachedTileEndpoint", "cacheTtl", out var value).Should().BeTrue();
        value.Should().Be("86400");
    }
}
