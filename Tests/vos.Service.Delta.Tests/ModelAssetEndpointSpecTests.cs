using FluentAssertions;
using vos.Service.Delta.Models;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Pins the keepsBy role and the ModelAsset kind in the catalog vocabulary. Keeping is a
// model decision, not a service default: a template reaching ModelAsset through keepsBy names the
// property that receives asset tickets and the Thing that carries them, while the root reaches no
// keeping kind — a model that says nothing keeps nothing, exactly today's transient behaviour.
public class ModelAssetEndpointSpecTests
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
            Thing("KeptTileEndpoint", new()
            {
                ["assetProperty"] = "",
                ["assetSubject"] = "",
                ["observedAtParameter"] = "",
            }),
        },
        Kinds = new()
        {
            new EndpointKind { Name = "ModelAsset", Requires = ["assetProperty", "assetSubject"] },
        },
        Relationships = new()
        {
            Is("KeptTileEndpoint", "Endpoint"),
            Uses("KeptTileEndpoint", EndpointKindRoles.Keeping, "ModelAsset"),
        },
    });

    [Fact]
    public void KeepsBy_IsARecognizedRole()
    {
        EndpointKindRoles.IsRole("keepsBy").Should().BeTrue();
        EndpointKindRoles.All.Should().Contain(EndpointKindRoles.Keeping);
    }

    [Fact]
    public void Keeping_ResolvedForTemplate_IsModelAssetWithItsRequirements()
    {
        var kind = Graph().ResolveKind("KeptTileEndpoint", EndpointKindRoles.Keeping);

        kind.Should().NotBeNull();
        kind!.Name.Should().Be("ModelAsset");
        kind.Requires.Should().BeEquivalentTo("assetProperty", "assetSubject");
    }

    [Fact]
    public void Keeping_RootReachesNoKind_SoTheModelKeepsNothingByDefault()
    {
        Graph().ResolveKind("Endpoint", EndpointKindRoles.Keeping).Should().BeNull();
    }

    [Fact]
    public void AssetTargets_AreStructuralBlanks_SoARegistrationOwesThem()
    {
        var graph = Graph();

        graph.AllowedKeys("KeptTileEndpoint").Should()
            .Contain("assetProperty").And.Contain("assetSubject").And.Contain("observedAtParameter");
        graph.TryGetEffectiveSeedValue("KeptTileEndpoint", "assetProperty", out _).Should().BeFalse();
        graph.TryGetEffectiveSeedValue("KeptTileEndpoint", "assetSubject", out _).Should().BeFalse();
    }
}
