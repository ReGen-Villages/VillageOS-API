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

    // A wire's ports live on the relationship, so a relationship read wrongly leaves the DAG builder two nodes with
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

    // A value written for a name the Thing's archetype declares is kept in the Thing's override store, not
    // among its own properties. A port the page saves onto a model whose port archetype declares portName
    // therefore read as a port with no name, and every wire into it was refused.
    [Fact]
    public void Parse_ReadsWhatAThingStatesInItsOverrideStore()
    {
        var port = Guid.NewGuid();
        var portArchetype = Guid.NewGuid();
        var json = $$"""
        {
          "snapshot": {
            "things": [
              { "Id": "{{port}}", "Name": "subject", "IsArchetype": false, "Properties": {},
                "InheritedOverrides": {
                  "{{portArchetype}}": {
                    "SourceId": "{{portArchetype}}", "SourceName": "Port", "InheritedAt": "2026-09-26T13:17:43Z",
                    "Properties": {
                      "portName": { "typeInfo": "vos.String", "value": "subject" },
                      "direction": { "typeInfo": "vos.String", "value": "out" } } } },
                "States": [], "Relationships": [] }
            ],
            "relationships": []
          }
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        graph.Thing(port)!.PropertyString("portName").Should().Be("subject");
        graph.Thing(port)!.PropertyString("direction").Should().Be("out");
    }

    // Own first: a name a Thing states both ways is read once, as its own.
    [Fact]
    public void Parse_LetsAnOwnValueWinOverAnOverride()
    {
        var thing = Guid.NewGuid();
        var json = $$"""
        {
          "things": [
            { "Id": "{{thing}}", "Name": "n",
              "Properties": { "portName": { "typeInfo": "vos.String", "value": "own" } },
              "InheritedOverrides": { "{{Guid.NewGuid()}}": { "SourceName": "Port",
                "Properties": { "portName": { "typeInfo": "vos.String", "value": "overridden" } } } } }
          ],
          "relationships": []
        }
        """;

        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        graph.Thing(thing)!.PropertyString("portName").Should().Be("own");
    }

    // A wire drawn as a relationship carries its ports on the relationship, which has an override store
    // of its own.
    [Fact]
    public void Parse_ReadsWhatARelationshipStatesInItsOverrideStore()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var carries = Guid.NewGuid();
        var wireArchetype = Guid.NewGuid();
        var isPredicate = Guid.NewGuid();
        var json = $$"""
        {
          "Snapshot": {
            "things": [
              { "Id": "{{from}}", "Name": "a", "Properties": {} },
              { "Id": "{{to}}", "Name": "b", "Properties": {} },
              { "Id": "{{isPredicate}}", "Name": "is", "Properties": {} },
              { "Id": "{{wireArchetype}}", "Name": "PipelineWire", "IsArchetype": true,
                "Properties": { "__IsPipelineWireArchetype": { "typeInfo": "vos.Boolean", "value": true } } },
              { "Id": "{{carries}}", "Name": "carries", "Properties": {} }
            ],
            "relationships": [
              { "Id": "{{Guid.NewGuid()}}", "SubjectId": "{{carries}}", "PredicateId": "{{isPredicate}}", "TargetId": "{{wireArchetype}}" },
              { "Id": "{{Guid.NewGuid()}}", "SubjectId": "{{from}}", "PredicateId": "{{carries}}", "TargetId": "{{to}}",
                "Properties": {},
                "InheritedOverrides": { "{{Guid.NewGuid()}}": { "SourceName": "PipelineWire",
                  "Properties": { "fromPort": { "typeInfo": "vos.String", "value": "echo" } } } } }
            ]
          }
        }
        """;

        // Wrapped under a capitalised key on purpose: the wrapper is read as the records are, without regard to case.
        var graph = SnapshotParser.Parse(JsonDocument.Parse(json).RootElement);

        var wire = graph.OutgoingByPredicateCarrying(graph.Thing(from)!, PipelineArchetypes.PipelineWireFlag).Single();
        wire.TargetId.Should().Be(to);
        wire.PropertyString("fromPort").Should().Be("echo");
    }

    // A relationship short of one of its four identifiers is dropped rather than failing the load, and the Things
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
