using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class PipelineGraphNamesTests
{
    private readonly GraphFixture _fixture = new();

    private (GraphThing Pipeline, GraphThing Node) HeldAlong(string predicateName)
    {
        var pipeline = _fixture.Thing("Harvest pipeline");
        var node = _fixture.Thing("Rain gauge node");
        _fixture.Rel(pipeline, _fixture.Thing(predicateName), node);
        return (pipeline, node);
    }

    [Fact]
    public void A_predicate_matched_by_name_is_matched_exactly()
    {
        var (pipeline, node) = HeldAlong("has");

        var graph = _fixture.Build();

        graph.OutgoingTargets(pipeline, "has").Should().ContainSingle().Which.Id.Should().Be(node.Id);
        graph.IncomingSubjects(node, "has").Should().ContainSingle().Which.Id.Should().Be(pipeline.Id);
        graph.RelationshipsInto(node, "has").Should().ContainSingle();
    }

    [Fact]
    public void A_predicate_whose_name_differs_only_in_case_is_another_predicate()
    {
        var (pipeline, node) = HeldAlong("Has");

        var graph = _fixture.Build();

        graph.OutgoingTargets(pipeline, "has").Should().BeEmpty("Thing names are matched exactly, case included");
        graph.IncomingSubjects(node, "has").Should().BeEmpty();
        graph.RelationshipsInto(node, "has").Should().BeEmpty();
    }
}
