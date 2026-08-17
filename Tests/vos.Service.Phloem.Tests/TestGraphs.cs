using System.Text.Json;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Tests;

// The two built-in predicates and the archetype for each role a pipeline is resolved from. The broker
// selects the archetypes by the flag each carries, so a snapshot holds all of them whether or not the
// pipeline in it has a node playing that role.
public sealed record PipelineVocabulary(
    GraphThing Is,
    GraphThing Has,
    GraphThing Pipeline,
    GraphThing PipelineNode,
    GraphThing Connection,
    GraphThing Service,
    GraphThing Port,
    GraphThing PipelineWire,
    GraphThing PipelineInput,
    GraphThing PipelineOutput);

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

    // An archetype carrying the flag that says which role it plays. The name is this fixture's own choice:
    // nothing in the orchestrator reads it (#6516).
    public GraphThing Archetype(string name, string roleFlag) => Thing(name, (roleFlag, true));

    public PipelineVocabulary DeclareVocabulary(string namePrefix = "")
    {
        var isPredicate = Thing("is");
        var has = Thing("has");
        var node = Archetype(namePrefix + "PipelineNode", PipelineArchetypes.PipelineNodeFlag);
        var input = Archetype(namePrefix + "PipelineInput", PipelineArchetypes.PipelineInputFlag);
        var output = Archetype(namePrefix + "PipelineOutput", PipelineArchetypes.PipelineOutputFlag);
        Rel(input, isPredicate, node);  // boundary archetypes ARE pipeline nodes (multiple inheritance)
        Rel(output, isPredicate, node);

        return new PipelineVocabulary(
            isPredicate,
            has,
            Archetype(namePrefix + "Pipeline", PipelineArchetypes.PipelineFlag),
            node,
            Archetype(namePrefix + "PlatformServiceConnection", PipelineArchetypes.ConnectionFlag),
            Archetype(namePrefix + "Service", PipelineArchetypes.ServiceFlag),
            Archetype(namePrefix + "Port", PipelineArchetypes.PortFlag),
            Archetype(namePrefix + "PipelineWire", PipelineArchetypes.PipelineWireFlag),
            input,
            output);
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
        var vocabulary = fx.DeclareVocabulary();
        var feeds = fx.Thing("feeds");
        fx.Rel(feeds, vocabulary.Is, vocabulary.PipelineWire); // the feeds predicate is a pipeline wire

        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, portIn);
        fx.Rel(proto, vocabulary.Has, portOut);

        var genSvc = fx.Thing("genSvc");
        fx.Rel(genSvc, vocabulary.Is, proto);
        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, vocabulary.Is, proto);

        var genConn = fx.Thing("genConn", ("Subdomain", "gen"));
        fx.Rel(genConn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(genConn, vocabulary.Has, genSvc);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(echConn, vocabulary.Has, echSvc);

        var gen = fx.Thing("Generate");
        fx.Rel(gen, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(gen, vocabulary.Has, genConn);
        var ech = fx.Thing("Echo");
        fx.Rel(ech, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(ech, vocabulary.Has, echConn);

        var pipe = fx.Thing("Demo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, gen);
        fx.Rel(pipe, vocabulary.Has, ech);

        fx.Rel(gen, feeds, ech, ("fromPort", "echo"), ("toPort", "message"));

        return (fx, pipe.Id);
    }

    // Boundary I/O demo (#5873): In (PipelineInput, out port `seed`) → Echo (message→echo) → Out
    // (PipelineOutput, in port `result`). The Input node's `seed` output is filled from the run param `seed`;
    // the value wired into the Output node's `result` input becomes the run's published result.
    public static (GraphFixture Fixture, Guid PipelineId) BoundaryPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var feeds = fx.Thing("feeds");
        fx.Rel(feeds, vocabulary.Is, vocabulary.PipelineWire);

        // Echo service node: message (in) → echo (out).
        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var portIn = fx.Thing("e.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("e.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, portIn);
        fx.Rel(proto, vocabulary.Has, portOut);
        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, vocabulary.Is, proto);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(echConn, vocabulary.Has, echSvc);
        var ech = fx.Thing("Echo");
        fx.Rel(ech, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(ech, vocabulary.Has, echConn);

        // Input boundary node with an output port `seed`.
        var input = fx.Thing("In");
        fx.Rel(input, vocabulary.Is, vocabulary.PipelineInput);
        var seedPort = fx.Thing("in.seed", ("direction", "out"), ("type", "string"), ("portName", "seed"));
        fx.Rel(seedPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(input, vocabulary.Has, seedPort);

        // Output boundary node with an input port `result`.
        var output = fx.Thing("Out");
        fx.Rel(output, vocabulary.Is, vocabulary.PipelineOutput);
        var resultPort = fx.Thing("out.result", ("direction", "in"), ("type", "string"), ("portName", "result"));
        fx.Rel(resultPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(output, vocabulary.Has, resultPort);

        var pipe = fx.Thing("BoundaryDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, input);
        fx.Rel(pipe, vocabulary.Has, ech);
        fx.Rel(pipe, vocabulary.Has, output);

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
        var vocabulary = fx.DeclareVocabulary();
        var feeds = fx.Thing("feeds");
        fx.Rel(feeds, vocabulary.Is, vocabulary.PipelineWire);

        var proto = InputOutputPrototype(fx, vocabulary, "IOProto");
        var a = ServiceNode(fx, vocabulary, proto, "A", "a");
        var b = ServiceNode(fx, vocabulary, proto, "B", "b");
        var c = ServiceNode(fx, vocabulary, proto, "C", "c");

        var pipe = fx.Thing("MergeDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, a);
        fx.Rel(pipe, vocabulary.Has, b);
        fx.Rel(pipe, vocabulary.Has, c);

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
        var vocabulary = fx.DeclareVocabulary();
        var feeds = fx.Thing("feeds");
        fx.Rel(feeds, vocabulary.Is, vocabulary.PipelineWire);

        var proto = InputOutputPrototype(fx, vocabulary, "IOProto");
        var a = ServiceNode(fx, vocabulary, proto, "A", "a");
        var c = ServiceNode(fx, vocabulary, proto, "C", "c");

        var pipe = fx.Thing("TransformDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, a);
        fx.Rel(pipe, vocabulary.Has, c);
        fx.Rel(a, feeds, c, ("fromPort", "out"), ("toPort", "in"), ("transform", transform));

        return (fx, pipe.Id);
    }

    // A single Echo node whose input port `message` is bound to the run param `greeting`
    // (no wires) — exercises run-level param routing (#5647).
    public static (GraphFixture Fixture, Guid PipelineId) ParamBoundPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();

        var proto = fx.Thing("EchoProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "message"), ("required", "true"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "echo"));
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, portIn);
        fx.Rel(proto, vocabulary.Has, portOut);

        var echSvc = fx.Thing("echSvc");
        fx.Rel(echSvc, vocabulary.Is, proto);
        var echConn = fx.Thing("echConn", ("Subdomain", "ech"));
        fx.Rel(echConn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(echConn, vocabulary.Has, echSvc);

        var ech = fx.Thing("Echo", ("paramBindings", "{\"message\":\"greeting\"}"));
        fx.Rel(ech, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(ech, vocabulary.Has, echConn);

        var pipe = fx.Thing("ParamDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, ech);

        return (fx, pipe.Id);
    }

    // The Water-reserve site-analysis pipeline (#5805): a single WaterReserve node whose three inputs
    // (population, perCapitaConsumptionM3, storageCapacityM3) are param-bound, so a run supplies them.
    public static (GraphFixture Fixture, Guid PipelineId) SiteAnalysisWaterPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();

        var proto = fx.Thing("WaterReserveProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        NumberPort(fx, vocabulary, proto, "population", "in", required: true);
        NumberPort(fx, vocabulary, proto, "perCapitaConsumptionM3", "in", required: true);
        NumberPort(fx, vocabulary, proto, "storageCapacityM3", "in", required: true);
        NumberPort(fx, vocabulary, proto, "daysOfSupply", "out");

        var svc = fx.Thing("waterSvc");
        fx.Rel(svc, vocabulary.Is, proto);
        var conn = fx.Thing("waterConn", ("Subdomain", "water-reserve"));
        fx.Rel(conn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(conn, vocabulary.Has, svc);

        var node = fx.Thing("WaterReserve",
            ("paramBindings", "{\"population\":\"population\",\"perCapitaConsumptionM3\":\"perCapitaConsumptionM3\",\"storageCapacityM3\":\"storageCapacityM3\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("SiteAnalysis");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    // The Energy site-analysis pipeline (#5806): an EnergyBalance node whose five inputs are param-bound,
    // so a run supplies them. Same shape as SiteAnalysisWaterPipeline — every analysis domain composes
    // the same way.
    public static (GraphFixture Fixture, Guid PipelineId) SiteAnalysisEnergyPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();

        var proto = fx.Thing("EnergyBalanceProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        NumberPort(fx, vocabulary, proto, "solarPvAreaM2", "in", required: true);
        NumberPort(fx, vocabulary, proto, "solarResourceKwhPerM2PerYear", "in", required: true);
        NumberPort(fx, vocabulary, proto, "moduleEfficiency", "in", required: true);
        NumberPort(fx, vocabulary, proto, "performanceRatio", "in", required: true);
        NumberPort(fx, vocabulary, proto, "otherGenerationMwhPerYear", "in", required: true);
        NumberPort(fx, vocabulary, proto, "annualConsumptionMwhPerYear", "in", required: true);
        NumberPort(fx, vocabulary, proto, "pctOfConsumption", "out");

        var svc = fx.Thing("energySvc");
        fx.Rel(svc, vocabulary.Is, proto);
        var conn = fx.Thing("energyConn", ("Subdomain", "energy-balance"));
        fx.Rel(conn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(conn, vocabulary.Has, svc);

        var node = fx.Thing("EnergyBalance",
            ("paramBindings", "{\"solarPvAreaM2\":\"solarPvAreaM2\",\"solarResourceKwhPerM2PerYear\":\"solarResourceKwhPerM2PerYear\",\"moduleEfficiency\":\"moduleEfficiency\",\"performanceRatio\":\"performanceRatio\",\"otherGenerationMwhPerYear\":\"otherGenerationMwhPerYear\",\"annualConsumptionMwhPerYear\":\"annualConsumptionMwhPerYear\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("EnergyAnalysis");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    // A single Scorer node whose input port `item` is a collection (fan-out, #5648) and `weight` is a
    // scalar (broadcast). Both inputs are param-bound (`item`→`items`, `weight`→`w`) so a run supplies the list.
    // onItemError sets the node's failure policy.
    public static (GraphFixture Fixture, Guid PipelineId) FanOutPipeline(string onItemError = "fail")
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();

        var proto = fx.Thing("ScoreProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var pItem = fx.Thing("p.item", ("direction", "in"), ("type", "string"), ("portName", "item"), ("collection", "true"));
        var pWeight = fx.Thing("p.weight", ("direction", "in"), ("type", "number"), ("portName", "weight"));
        var pScore = fx.Thing("p.score", ("direction", "out"), ("type", "string"), ("portName", "score"));
        fx.Rel(pItem, vocabulary.Is, vocabulary.Port);
        fx.Rel(pWeight, vocabulary.Is, vocabulary.Port);
        fx.Rel(pScore, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, pItem);
        fx.Rel(proto, vocabulary.Has, pWeight);
        fx.Rel(proto, vocabulary.Has, pScore);

        var svc = fx.Thing("scoreSvc");
        fx.Rel(svc, vocabulary.Is, proto);
        var conn = fx.Thing("scoreConn", ("Subdomain", "score"));
        fx.Rel(conn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(conn, vocabulary.Has, svc);

        var node = fx.Thing("Scorer", ("onItemError", onItemError), ("paramBindings", "{\"item\":\"items\",\"weight\":\"w\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("FanDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    private static GraphThing InputOutputPrototype(GraphFixture fx, PipelineVocabulary vocabulary, string name)
    {
        var proto = fx.Thing(name);
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "any"), ("portName", "in"));
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "any"), ("portName", "out"));
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, portIn);
        fx.Rel(proto, vocabulary.Has, portOut);
        return proto;
    }

    private static GraphThing ServiceNode(
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing prototype, string name, string subdomain)
    {
        var service = fx.Thing($"{name}Svc");
        fx.Rel(service, vocabulary.Is, prototype);
        var connection = fx.Thing($"{name}Conn", ("Subdomain", subdomain));
        fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
        fx.Rel(connection, vocabulary.Has, service);
        var node = fx.Thing(name);
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, connection);
        return node;
    }

    private static void NumberPort(
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing prototype, string name, string direction, bool required = false)
    {
        var props = required
            ? new (string, object)[] { ("direction", direction), ("type", "number"), ("portName", name), ("required", "true") }
            : new (string, object)[] { ("direction", direction), ("type", "number"), ("portName", name) };
        var port = fx.Thing($"p.{name}", props);
        fx.Rel(port, vocabulary.Is, vocabulary.Port);
        fx.Rel(prototype, vocabulary.Has, port);
    }
}
