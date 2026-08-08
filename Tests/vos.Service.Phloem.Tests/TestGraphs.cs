using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Tests;

// Builds a PipelineGraph programmatically for tests — Things + Relationships with
// the same shapes Phloem reads from a Mycelium snapshot.
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

// The canonical demo: Generate –feeds(echo→message)→ Echo. Each node binds a Connection (subdomain)
// → Service → shared EchoProto prototype with typed ports.
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

    // Boundary I/O demo (#5873): In (PipelineInput, out port `seed`) → Echo (message→echo) → Out
    // (PipelineOutput, in port `result`). The Input node's `seed` output is filled from the run param `seed`;
    // the value wired into the Output node's `result` input becomes the run's published result.
    public static (GraphFixture Fixture, Guid PipelineId) BoundaryPipeline()
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
        var inArch = fx.Thing("PipelineInput");
        var outArch = fx.Thing("PipelineOutput");
        fx.Rel(feeds, isP, wireArch);
        fx.Rel(inArch, isP, nodeArch);  // boundary archetypes ARE pipeline nodes (multiple inheritance)
        fx.Rel(outArch, isP, nodeArch);

        // Echo service node: message (in) → echo (out).
        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, isP, svcArch);
        var portIn = fx.Thing("e.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("e.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, isP, portArch);
        fx.Rel(portOut, isP, portArch);
        fx.Rel(proto, has, portIn);
        fx.Rel(proto, has, portOut);
        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, isP, proto);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, isP, connArch);
        fx.Rel(echConn, has, echSvc);
        var ech = fx.Thing("Echo");
        fx.Rel(ech, isP, nodeArch);
        fx.Rel(ech, has, echConn);

        // Input boundary node with an output port `seed`.
        var input = fx.Thing("In");
        fx.Rel(input, isP, inArch);
        var seedPort = fx.Thing("in.seed", ("direction", "out"), ("type", "string"), ("portName", "seed"));
        fx.Rel(seedPort, isP, portArch);
        fx.Rel(input, has, seedPort);

        // Output boundary node with an input port `result`.
        var output = fx.Thing("Out");
        fx.Rel(output, isP, outArch);
        var resultPort = fx.Thing("out.result", ("direction", "in"), ("type", "string"), ("portName", "result"));
        fx.Rel(resultPort, isP, portArch);
        fx.Rel(output, has, resultPort);

        var pipe = fx.Thing("BoundaryDemo");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, input);
        fx.Rel(pipe, has, ech);
        fx.Rel(pipe, has, output);

        fx.Rel(input, feeds, ech, ("fromPort", "seed"), ("toPort", "message"));
        fx.Rel(ech, feeds, output, ("fromPort", "echo"), ("toPort", "result"));

        return (fx, pipe.Id);
    }

    // Field-merge demo (#5874): A and B both feed node C's single `in` input, A at to-path `a` and B
    // at to-path `b`, so C receives the two outputs deep-merged into one object. All three nodes share an IO
    // prototype with an `in` input and an `out` output.
    public static (GraphFixture Fixture, Guid PipelineId) FieldMergePipeline()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");
        var feeds = fx.Thing("feeds");

        var pipelineArchetype = fx.Thing("Pipeline");
        var nodeArchetype = fx.Thing("PipelineNode");
        var connectionArchetype = fx.Thing("PlatformServiceConnection");
        var serviceArchetype = fx.Thing("Service");
        var portArchetype = fx.Thing("Port");
        var wireArchetype = fx.Thing("PipelineWire");
        fx.Rel(feeds, isP, wireArchetype);

        var proto = fx.Thing("IOProto");
        fx.Rel(proto, isP, serviceArchetype);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "any"), ("portName", "in"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "any"), ("portName", "out"));
        fx.Rel(portIn, isP, portArchetype);
        fx.Rel(portOut, isP, portArchetype);
        fx.Rel(proto, has, portIn);
        fx.Rel(proto, has, portOut);

        GraphThing Node(string name, string subdomain)
        {
            var service = fx.Thing($"{name}Svc");
            fx.Rel(service, isP, proto);
            var connection = fx.Thing($"{name}Conn", ("Subdomain", subdomain));
            fx.Rel(connection, isP, connectionArchetype);
            fx.Rel(connection, has, service);
            var node = fx.Thing(name);
            fx.Rel(node, isP, nodeArchetype);
            fx.Rel(node, has, connection);
            return node;
        }

        var a = Node("A", "a");
        var b = Node("B", "b");
        var c = Node("C", "c");

        var pipe = fx.Thing("MergeDemo");
        fx.Rel(pipe, isP, pipelineArchetype);
        fx.Rel(pipe, has, a);
        fx.Rel(pipe, has, b);
        fx.Rel(pipe, has, c);

        // Both wires target C.in, but land at different to-paths so they deep-merge instead of overwriting.
        fx.Rel(a, feeds, c, ("fromPort", "out"), ("toPort", "in"), ("toPath", "a"));
        fx.Rel(b, feeds, c, ("fromPort", "out"), ("toPort", "in"), ("toPath", "b"));

        return (fx, pipe.Id);
    }

    // Wire-transform demo (#5875): A → C over one wire carrying a JSONata transform
    // that reshapes A's output before it reaches C's `in` input.
    public static (GraphFixture Fixture, Guid PipelineId) WireTransformPipeline(string transform)
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");
        var feeds = fx.Thing("feeds");

        var pipelineArchetype = fx.Thing("Pipeline");
        var nodeArchetype = fx.Thing("PipelineNode");
        var connectionArchetype = fx.Thing("PlatformServiceConnection");
        var serviceArchetype = fx.Thing("Service");
        var portArchetype = fx.Thing("Port");
        var wireArchetype = fx.Thing("PipelineWire");
        fx.Rel(feeds, isP, wireArchetype);

        var proto = fx.Thing("IOProto");
        fx.Rel(proto, isP, serviceArchetype);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "any"), ("portName", "in"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "any"), ("portName", "out"));
        fx.Rel(portIn, isP, portArchetype);
        fx.Rel(portOut, isP, portArchetype);
        fx.Rel(proto, has, portIn);
        fx.Rel(proto, has, portOut);

        GraphThing Node(string name, string subdomain)
        {
            var service = fx.Thing($"{name}Svc");
            fx.Rel(service, isP, proto);
            var connection = fx.Thing($"{name}Conn", ("Subdomain", subdomain));
            fx.Rel(connection, isP, connectionArchetype);
            fx.Rel(connection, has, service);
            var node = fx.Thing(name);
            fx.Rel(node, isP, nodeArchetype);
            fx.Rel(node, has, connection);
            return node;
        }

        var a = Node("A", "a");
        var c = Node("C", "c");

        var pipe = fx.Thing("TransformDemo");
        fx.Rel(pipe, isP, pipelineArchetype);
        fx.Rel(pipe, has, a);
        fx.Rel(pipe, has, c);
        fx.Rel(a, feeds, c, ("fromPort", "out"), ("toPort", "in"), ("transform", transform));

        return (fx, pipe.Id);
    }

    // A single Echo node whose input port `message` is bound to the run param `greeting`
    // (no wires) — exercises run-level param routing (#5647).
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

    // The Energy site-analysis pipeline (#5806): an EnergyBalance node whose five inputs are param-bound,
    // so a run supplies them. Same shape as SiteAnalysisWaterPipeline — every analysis domain composes
    // the same way.
    public static (GraphFixture Fixture, Guid PipelineId) SiteAnalysisEnergyPipeline()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");

        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var connArch = fx.Thing("PlatformServiceConnection");
        var svcArch = fx.Thing("Service");
        var portArch = fx.Thing("Port");

        var proto = fx.Thing("EnergyBalanceProto");
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
        Port("solarPvAreaM2", "in", required: true);
        Port("solarResourceKwhPerM2PerYear", "in", required: true);
        Port("moduleEfficiency", "in", required: true);
        Port("performanceRatio", "in", required: true);
        Port("otherGenerationMwhPerYear", "in", required: true);
        Port("annualConsumptionMwhPerYear", "in", required: true);
        Port("pctOfConsumption", "out");

        var svc = fx.Thing("energySvc");
        fx.Rel(svc, isP, proto);
        var conn = fx.Thing("energyConn", ("Subdomain", "energy-balance"));
        fx.Rel(conn, isP, connArch);
        fx.Rel(conn, has, svc);

        var node = fx.Thing("EnergyBalance",
            ("paramBindings", "{\"solarPvAreaM2\":\"solarPvAreaM2\",\"solarResourceKwhPerM2PerYear\":\"solarResourceKwhPerM2PerYear\",\"moduleEfficiency\":\"moduleEfficiency\",\"performanceRatio\":\"performanceRatio\",\"otherGenerationMwhPerYear\":\"otherGenerationMwhPerYear\",\"annualConsumptionMwhPerYear\":\"annualConsumptionMwhPerYear\"}"));
        fx.Rel(node, isP, nodeArch);
        fx.Rel(node, has, conn);

        var pipe = fx.Thing("EnergyAnalysis");
        fx.Rel(pipe, isP, pipelineArch);
        fx.Rel(pipe, has, node);

        return (fx, pipe.Id);
    }

    // A single Scorer node whose input port `item` is a collection (fan-out, #5648) and `weight` is a
    // scalar (broadcast). Both inputs are param-bound (`item`→`items`, `weight`→`w`) so a run supplies the list.
    // onItemError sets the node's failure policy.
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
