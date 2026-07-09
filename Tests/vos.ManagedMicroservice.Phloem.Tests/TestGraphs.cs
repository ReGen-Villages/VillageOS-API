using System.Text.Json;
using vos.ManagedMicroservice.Phloem.Model;

namespace vos.ManagedMicroservice.Phloem.Tests;

/// <summary>Builds a <see cref="PipelineGraph"/> programmatically for tests — Things + Relationships with
/// the same shapes Phloem reads from a Mycelium snapshot.</summary>
public sealed class GraphFixture
{
    private readonly Dictionary<Guid, GraphThing> _things = new();
    private readonly List<GraphRelationship> _rels = new();
    private readonly Dictionary<string, GraphThing> _byName = new(StringComparer.Ordinal);

    public GraphThing Thing(string name, params (string Name, object Value)[] props)
    {
        var t = new GraphThing
        {
            Id = Guid.NewGuid(),
            Name = name,
            Properties = props.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value), StringComparer.OrdinalIgnoreCase),
        };
        _things[t.Id] = t;
        _byName[name] = t;
        return t;
    }

    public GraphThing Get(string name) => _byName[name];

    public void Rel(GraphThing subject, GraphThing predicate, GraphThing target, params (string Name, object Value)[] props)
        => _rels.Add(new GraphRelationship
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            PredicateId = predicate.Id,
            TargetId = target.Id,
            Properties = props.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value), StringComparer.OrdinalIgnoreCase),
        });

    public PipelineGraph Build() => new(_things, _rels);
}

/// <summary>The canonical demo: Generate –feeds(echo→message)→ Echo. Each node binds a Connection (subdomain)
/// → Service → shared EchoProto prototype with typed ports.</summary>
public static class TestGraphs
{
    public static (GraphFixture Fixture, Guid PipelineId) DemoPipeline()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");
        var feeds = fx.Thing("feeds");

        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var connArch = fx.Thing("PlatformServiceConnection");
        var svcArch = fx.Thing("Service");
        var portArch = fx.Thing("Port");
        var wireArch = fx.Thing("PipelineWire");
        fx.Rel(feeds, isP, wireArch); // the feeds predicate is a PipelineWire

        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, isP, svcArch);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, isP, portArch);
        fx.Rel(portOut, isP, portArch);
        fx.Rel(proto, has, portIn);
        fx.Rel(proto, has, portOut);

        var genSvc = fx.Thing("genSvc");
        fx.Rel(genSvc, isP, proto);
        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, isP, proto);

        var genConn = fx.Thing("genConn", ("Subdomain", "gen"));
        fx.Rel(genConn, isP, connArch);
        fx.Rel(genConn, has, genSvc);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, isP, connArch);
        fx.Rel(echConn, has, echSvc);

        var gen = fx.Thing("Generate");
        fx.Rel(gen, isP, nodeArch);
        fx.Rel(gen, has, genConn);
        var ech = fx.Thing("Echo");
        fx.Rel(ech, isP, nodeArch);
        fx.Rel(ech, has, echConn);

        var pipe = fx.Thing("Demo");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, gen);
        fx.Rel(pipe, has, ech);

        fx.Rel(gen, feeds, ech, ("fromPort", "echo"), ("toPort", "message"));

        return (fx, pipe.Id);
    }

    /// <summary>A single Echo node whose input port `message` is bound to the run param `greeting`
    /// (no wires) — exercises run-level param routing (#5647).</summary>
    public static (GraphFixture Fixture, Guid PipelineId) ParamBoundPipeline()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");

        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var connArch = fx.Thing("PlatformServiceConnection");
        var svcArch = fx.Thing("Service");
        var portArch = fx.Thing("Port");

        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, isP, svcArch);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, isP, portArch);
        fx.Rel(portOut, isP, portArch);
        fx.Rel(proto, has, portIn);
        fx.Rel(proto, has, portOut);

        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, isP, proto);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, isP, connArch);
        fx.Rel(echConn, has, echSvc);

        var ech = fx.Thing("Echo", ("paramBindings", "{\"message\":\"greeting\"}"));
        fx.Rel(ech, isP, nodeArch);
        fx.Rel(ech, has, echConn);

        var pipe = fx.Thing("ParamDemo");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, ech);

        return (fx, pipe.Id);
    }

    // The Water-reserve site-analysis pipeline (#5805): a single WaterReserve node whose three inputs
    // (population, perCapitaConsumptionM3, storageCapacityM3) are param-bound, so a run supplies them.
    public static (GraphFixture Fixture, Guid PipelineId) SiteAnalysisWaterPipeline()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");

        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var connArch = fx.Thing("PlatformServiceConnection");
        var svcArch = fx.Thing("Service");
        var portArch = fx.Thing("Port");

        var proto = fx.Thing("WaterReserveProto");
        fx.Rel(proto, isP, svcArch);

        void Port(string name, string direction, bool required = false)
        {
            var props = required
                ? new (string, object)[] { ("direction", direction), ("type", "number"), ("portName", name), ("required", "true") }
                : new (string, object)[] { ("direction", direction), ("type", "number"), ("portName", name) };
            var p = fx.Thing($"p.{name}", props);
            fx.Rel(p, isP, portArch);
            fx.Rel(proto, has, p);
        }
        Port("population", "in", required: true);
        Port("perCapitaConsumptionM3", "in", required: true);
        Port("storageCapacityM3", "in", required: true);
        Port("daysOfSupply", "out");

        var svc = fx.Thing("waterSvc");
        fx.Rel(svc, isP, proto);
        var conn = fx.Thing("waterConn", ("Subdomain", "water-reserve"));
        fx.Rel(conn, isP, connArch);
        fx.Rel(conn, has, svc);

        var node = fx.Thing("WaterReserve",
            ("paramBindings", "{\"population\":\"population\",\"perCapitaConsumptionM3\":\"perCapitaConsumptionM3\",\"storageCapacityM3\":\"storageCapacityM3\"}"));
        fx.Rel(node, isP, nodeArch);
        fx.Rel(node, has, conn);

        var pipe = fx.Thing("SiteAnalysis");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, node);

        return (fx, pipe.Id);
    }

    /// <summary>A single Scorer node whose input port `item` is a collection (fan-out, #5648) and `weight` is a
    /// scalar (broadcast). Both inputs are param-bound (`item`→`items`, `weight`→`w`) so a run supplies the list.
    /// <paramref name="onItemError"/> sets the node's failure policy.</summary>
    public static (GraphFixture Fixture, Guid PipelineId) FanOutPipeline(string onItemError = "fail")
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");

        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var connArch = fx.Thing("PlatformServiceConnection");
        var svcArch = fx.Thing("Service");
        var portArch = fx.Thing("Port");

        var proto = fx.Thing("ScoreProto");
        fx.Rel(proto, isP, svcArch);
        var pItem = fx.Thing("p.item", ("direction", "in"), ("type", "string"), ("portName", "item"), ("collection", "true"));
        var pWeight = fx.Thing("p.weight", ("direction", "in"), ("type", "number"), ("portName", "weight"));
        var pScore = fx.Thing("p.score", ("direction", "out"), ("type", "string"), ("portName", "score"));
        fx.Rel(pItem, isP, portArch);
        fx.Rel(pWeight, isP, portArch);
        fx.Rel(pScore, isP, portArch);
        fx.Rel(proto, has, pItem);
        fx.Rel(proto, has, pWeight);
        fx.Rel(proto, has, pScore);

        var svc = fx.Thing("scoreSvc");
        fx.Rel(svc, isP, proto);
        var conn = fx.Thing("scoreConn", ("Subdomain", "score"));
        fx.Rel(conn, isP, connArch);
        fx.Rel(conn, has, svc);

        var node = fx.Thing("Scorer", ("onItemError", onItemError), ("paramBindings", "{\"item\":\"items\",\"weight\":\"w\"}"));
        fx.Rel(node, isP, nodeArch);
        fx.Rel(node, has, conn);

        var pipe = fx.Thing("FanDemo");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, node);

        return (fx, pipe.Id);
    }
}
