using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// Parsing a Mycelium subscription snapshot into the queryable graph, unwrapping {value,typeInfo} property envelopes.
public class SnapshotParserTests
{
    // Fixtured as SnapshotBuilder emits it, fields this parser ignores included: the broker serialises with
    // no naming policy, so a member kept in the wrong case here is a snapshot that reads as an empty model.
    [Fact]
    public void Parse_ReadsTheSnapshotAsTheBrokerWritesIt()
    {
        var pipeline = Guid.NewGuid();
        var archetype = Guid.NewGuid();
        var isPredicate = Guid.NewGuid();
        var relationship = Guid.NewGuid();
        var json = $$"""
        {
          "subscriptionId": "{{Guid.NewGuid()}}",
          "watermark": 42,
          "snapshot": {
            "watermark": 42,
            "things": [
              { "Id": "{{pipeline}}", "Name": "Demo", "IsArchetype": false,
                "Properties": { "Subdomain": { "typeInfo": "vos.String", "value": "gen" } },
                "RollupProperties": {}, "InheritedOverrides": {}, "States": [],
                "Relationships": [ "{{relationship}}" ] },
              { "Id": "{{archetype}}", "Name": "Pipeline", "IsArchetype": true,
                "Properties": { "__IsPipelineArchetype": { "typeInfo": "vos.Boolean", "value": true } },
                "RollupProperties": {}, "InheritedOverrides": {}, "States": [],
                "Relationships": [ "{{relationship}}" ] },
              { "Id": "{{isPredicate}}", "Name": "is", "IsArchetype": false,
                "Properties": {}, "RollupProperties": {}, "InheritedOverrides": {}, "States": [],
                "Relationships": [] }
            ],
            "relationships": [
              { "Id": "{{relationship}}", "Name": null,
                "SubjectId": "{{pipeline}}", "PredicateId": "{{isPredicate}}", "TargetId": "{{archetype}}",
                "Properties": {}, "InheritedOverrides": {}, "States": [] }
            ]
          }
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        graph.Things.Should().HaveCount(3);
        graph.Thing(pipeline)!.PropertyString("Subdomain").Should().Be("gen");
        graph.ArchetypeCarrying(PipelineArchetypes.PipelineFlag)!.Id.Should().Be(archetype);
        graph.IsOfArchetypeCarrying(graph.Thing(pipeline)!, PipelineArchetypes.PipelineFlag).Should().BeTrue();
    }

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

    // A wire's ports live on the edge, so a relationship read wrongly leaves the DAG builder two nodes with
    // nothing between them.
    [Fact]
    public void Parse_ReadsEdgePropertiesFromABrokerWrittenRelationship()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var carries = Guid.NewGuid();
        var wireArchetype = Guid.NewGuid();
        var isPredicate = Guid.NewGuid();
        var json = $$"""
        {
          "snapshot": {
            "things": [
              { "Id": "{{from}}", "Name": "Generate", "Properties": {} },
              { "Id": "{{to}}", "Name": "Echo", "Properties": {} },
              { "Id": "{{isPredicate}}", "Name": "is", "Properties": {} },
              { "Id": "{{wireArchetype}}", "Name": "PipelineWire", "IsArchetype": true,
                "Properties": { "__IsPipelineWireArchetype": { "typeInfo": "vos.Boolean", "value": true } } },
              { "Id": "{{carries}}", "Name": "carries", "Properties": {} }
            ],
            "relationships": [
              { "Id": "{{Guid.NewGuid()}}", "SubjectId": "{{carries}}", "PredicateId": "{{isPredicate}}",
                "TargetId": "{{wireArchetype}}", "Properties": {} },
              { "Id": "{{Guid.NewGuid()}}", "SubjectId": "{{from}}", "PredicateId": "{{carries}}", "TargetId": "{{to}}",
                "Properties": {
                  "fromPort": { "typeInfo": "vos.String", "value": "echo" },
                  "toPort": { "typeInfo": "vos.String", "value": "message" } } }
            ]
          }
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        var wire = graph.OutgoingByPredicateCarrying(graph.Thing(from)!, PipelineArchetypes.PipelineWireFlag).Single();
        wire.TargetId.Should().Be(to);
        wire.PropertyString("fromPort").Should().Be("echo");
        wire.PropertyString("toPort").Should().Be("message");
    }

    // An edge short of one of its four identifiers is dropped rather than failing the load, and the Things
    // around it still parse. Fixtured bare, without the subscription answer's wrapper, which the parser
    // also accepts.
    [Fact]
    public void Parse_SkipsARelationshipMissingAnIdentifierAndKeepsTheRest()
    {
        var subject = Guid.NewGuid();
        var predicate = Guid.NewGuid();
        var target = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var json = $$"""
        {
          "things": [
            { "Id": "{{subject}}", "Properties": {} },
            { "Id": "{{predicate}}", "Name": "is", "Properties": {} },
            { "Id": "{{target}}", "Name": "Pipeline", "Properties": {} }
          ],
          "relationships": [
            { "Id": "{{Guid.NewGuid()}}", "SubjectId": "{{subject}}", "PredicateId": "{{predicate}}" },
            { "Id": "{{kept}}", "SubjectId": "{{subject}}", "PredicateId": "{{predicate}}", "TargetId": "{{target}}" }
          ]
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        graph.Things.Should().HaveCount(3);
        graph.Thing(subject)!.Name.Should().BeEmpty();
        graph.OutgoingTargets(graph.Thing(subject)!, "is").Single().Id.Should().Be(target);
    }
}
