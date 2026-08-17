using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// Parsing a Mycelium subscription snapshot into the queryable graph, unwrapping {value,type} property envelopes.
public class SnapshotParserTests
{
    [Fact]
    public void Parse_UnwrapsThingAndRelationshipPropertyValues()
    {
        var pipe = Guid.NewGuid();
        var arch = Guid.NewGuid();
        var isPred = Guid.NewGuid();
        var rel = Guid.NewGuid();
        var json = $$"""
        {
          "snapshot": {
            "things": [
              { "id": "{{pipe}}", "name": "Demo", "properties": { "Subdomain": { "value": "gen", "type": "vos.String" } } },
              { "id": "{{arch}}", "name": "Pipeline",
                "properties": { "__IsPipelineArchetype": { "value": true, "type": "vos.Boolean" } } },
              { "id": "{{isPred}}", "name": "is", "properties": {} }
            ],
            "relationships": [
              { "id": "{{rel}}", "subjectId": "{{pipe}}", "predicateId": "{{isPred}}", "targetId": "{{arch}}",
                "properties": { "fromPort": { "value": "echo", "type": "vos.String" } } }
            ]
          }
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        graph.Things.Should().HaveCount(3);
        graph.Thing(pipe)!.PropertyString("Subdomain").Should().Be("gen");
        // the is-relationship is readable as the role the archetype above it is marked with
        graph.IsOfArchetypeCarrying(graph.Thing(pipe)!, PipelineArchetypes.PipelineFlag).Should().BeTrue();
    }
}
