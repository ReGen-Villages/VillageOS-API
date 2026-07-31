using FluentAssertions;
using vos.Service.Delta.Models;
using Xunit;

namespace vos.Service.Delta.Tests.Models;

// Unit tests for EndpointSeedGraph.Build — structural validation of the
// endpoint-template graph (Feature #5465 / Task #5466). Parentage is derived from the seed's
// is relationships, not a scalar field. Each test crafts an input that violates exactly one
// rule so the failure reason is unambiguous.
public class EndpointSeedGraphTests
{
    private static RegisterEndpointRequest Thing(string name) =>
        new() { Name = name, Properties = new Dictionary<string, object>() };

    private static SeedRelationship Is(string subject, string target) =>
        new() { Subject = subject, Predicate = "is", Target = target };

    private static EndpointSeedModel Model(RegisterEndpointRequest[] things, params SeedRelationship[] rels) =>
        new() { Things = things.ToList(), Relationships = rels.ToList() };

    [Fact]
    public void Build_SingleRootThing_PopulatesTemplatesAndRoot()
    {
        var graph = EndpointSeedGraph.Build(Model(new[] { Thing("Endpoint") }));

        graph.Root.Name.Should().Be("Endpoint");
        graph.Templates.Should().HaveCount(1);
        graph.Templates.Should().ContainKey("Endpoint");
        graph.ParentName("Endpoint").Should().BeNull();
    }

    [Fact]
    public void Build_RootAndChild_DerivesParentFromIsRelationship()
    {
        var graph = EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("EsriEndpoint") },
            Is("EsriEndpoint", "Endpoint")));

        graph.Root.Name.Should().Be("Endpoint");
        graph.Templates.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint" });
        graph.ParentName("EsriEndpoint").Should().Be("Endpoint");
    }

    [Fact]
    public void Build_TemplateLookupIsCaseInsensitive()
    {
        var graph = EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("EsriEndpoint") },
            Is("EsriEndpoint", "Endpoint")));

        graph.Templates.ContainsKey("esriendpoint").Should().BeTrue();
    }

    [Fact]
    public void Build_DuplicateThingName_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(new[] { Thing("Endpoint"), Thing("Endpoint") }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate template name*");
    }

    [Fact]
    public void Build_DuplicateNameDifferentCase_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(new[] { Thing("Endpoint"), Thing("endpoint") }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate template name*");
    }

    [Fact]
    public void Build_NoRootTemplate_Throws()
    {
        // Mutual 'is' → no thing without a parent.
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("A"), Thing("B") },
            Is("A", "B"), Is("B", "A")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*no root template*");
    }

    [Fact]
    public void Build_MultipleRootTemplates_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(new[] { Thing("Endpoint"), Thing("OtherRoot") }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*multiple root templates*");
    }

    [Fact]
    public void Build_CycleAmongTemplates_Throws()
    {
        // Valid root present, but a disconnected A->B->A cycle.
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("A"), Thing("B") },
            Is("A", "B"), Is("B", "A")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cycle detected*");
    }

    [Fact]
    public void Build_RelationshipToUnknownTemplate_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("EsriEndpoint") },
            Is("EsriEndpoint", "Nonexistent")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown template*");
    }

    [Fact]
    public void Build_TemplateWithTwoIsParents_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("Other"), Thing("Child") },
            Is("Child", "Endpoint"), Is("Child", "Other")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than one 'is' parent*");
    }

    [Fact]
    public void Build_EmptyThingName_Throws()
    {
        var act = () => EndpointSeedGraph.Build(Model(new[] { Thing("") }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty name*");
    }

    [Fact]
    public void Build_NoThings_Throws()
    {
        var act = () => EndpointSeedGraph.Build(new EndpointSeedModel());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_NonIsRelationshipToUnknownTemplate_Throws()
    {
        // Even non-'is' relationships must reference known things.
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint") },
            new SeedRelationship { Subject = "Endpoint", Predicate = "observes", Target = "Ghost" }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown template*");
    }

    [Fact]
    public void Build_DeepValidChain_DoesNotThrow()
    {
        // CountyParcels is EsriEndpoint is Endpoint(root): a 3-level chain must validate.
        var act = () => EndpointSeedGraph.Build(Model(
            new[] { Thing("Endpoint"), Thing("EsriEndpoint"), Thing("CountyParcels") },
            Is("EsriEndpoint", "Endpoint"), Is("CountyParcels", "EsriEndpoint")));

        act.Should().NotThrow();
    }
}
