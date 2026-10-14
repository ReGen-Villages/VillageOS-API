using System.Text.Json;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;

namespace vos.Service.Phloem.Tests;

// The two built-in predicates and the archetype for each role a pipeline is resolved from. The broker
// selects the role Things by identifier, so a snapshot holds all of them whether or not the pipeline in it
// has a node playing that role.
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

    private GraphThing? _binds;

    // The role Thing, under the identifier every model gives it, binding the Thing that plays the role. The
    // played Thing's name is this fixture's own choice: nothing in the orchestrator reads it.
    public GraphThing Playing(string name, Role role, params (string Name, object Value)[] props)
    {
        _binds ??= Thing(ModelNames.Binds);
        var roleThing = new GraphThing { Id = role.Id, Name = $"{role.Word} role" };
        _things[roleThing.Id] = roleThing;
        _byName[roleThing.Name] = roleThing;
        var played = Thing(name, props);
        Rel(roleThing, _binds, played);
        return played;
    }

    public GraphThing Archetype(string name, Role role) => Playing(name, role);

    public PipelineVocabulary DeclareVocabulary(string namePrefix = "")
    {
        var isPredicate = Thing("is");
        var has = Thing("has");
        var node = Archetype(namePrefix + "PipelineNode", PipelineRoles.PipelineNode);
        var input = Archetype(namePrefix + "PipelineInput", PipelineRoles.PipelineInput);
        var output = Archetype(namePrefix + "PipelineOutput", PipelineRoles.PipelineOutput);
        Rel(input, isPredicate, node);  // boundary archetypes ARE pipeline nodes (multiple inheritance)
        Rel(output, isPredicate, node);

        return new PipelineVocabulary(
            isPredicate,
            has,
            Archetype(namePrefix + "Pipeline", PipelineRoles.Pipeline),
            node,
            Archetype(namePrefix + "PlatformServiceConnection", PipelineRoles.Connection),
            Archetype(namePrefix + "Service", PipelineRoles.Service),
            Archetype(namePrefix + "Port", PipelineRoles.Port),
            Archetype(namePrefix + "PipelineWire", PipelineRoles.PipelineWire),
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

// The canonical demo: Generate –carries(echo→message)→ Echo. Each node binds a Connection (subdomain)
// → Service → shared EchoProto prototype with typed ports.
public static class TestGraphs
{
    public static (GraphFixture Fixture, Guid PipelineId) DemoPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire); // the carries predicate is a pipeline wire

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

        fx.Rel(gen, carries, ech, ("fromPort", "echo"), ("toPort", "message"));

        return (fx, pipe.Id);
    }

    // Boundary I/O demo: In (PipelineInput, out port `seed`) → Echo (message→echo) → Out
    // (PipelineOutput, in port `result`). The Input node's `seed` output is filled from the run param `seed`;
    // the value wired into the Output node's `result` input becomes the run's published result.
    public static (GraphFixture Fixture, Guid PipelineId) BoundaryPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

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

        var input = fx.Thing("In");
        fx.Rel(input, vocabulary.Is, vocabulary.PipelineInput);
        var seedPort = fx.Thing("in.seed", ("direction", "out"), ("type", "string"), ("portName", "seed"));
        fx.Rel(seedPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(input, vocabulary.Has, seedPort);

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

        fx.Rel(input, carries, ech, ("fromPort", "seed"), ("toPort", "message"));
        fx.Rel(ech, carries, output, ("fromPort", "echo"), ("toPort", "result"));

        return (fx, pipe.Id);
    }

    public sealed record TellingFixture(GraphFixture Fixture, Guid PipelineId, Guid SystemId, Guid ConnectionId);

    // BoundaryPipeline whose end node stands for an external system. Unless told otherwise the system names
    // the connection it is told through, along the predicate bound to that role.
    public static TellingFixture TellingPipeline(bool namesItsConnection = true)
    {
        var (fx, pipelineId) = BoundaryPipeline();
        var isPredicate = fx.Get("is");
        var standsFor = fx.Playing("standsFor", PipelineRoles.StandsFor);
        var toldThrough = fx.Playing("toldThrough", PipelineRoles.ToldThrough);
        var systemKind = fx.Archetype("Neighbour", PipelineRoles.ExternalSystem);
        var system = fx.Thing("Reporting office");
        fx.Rel(system, isPredicate, systemKind);
        fx.Rel(fx.Get("Out"), standsFor, system);
        var tells = fx.Thing("tellsReportingOffice");
        fx.Rel(tells, isPredicate, fx.Get("PlatformServiceConnection"));
        if (namesItsConnection) fx.Rel(system, toldThrough, tells);
        return new TellingFixture(fx, pipelineId, system.Id, tells.Id);
    }

    // The three ways a dispatched relationship's target can start a pipeline, and one way it cannot.
    public sealed record StartFixture(
        GraphFixture Fixture,
        Guid DrawnPipelineId,
        Guid WatchingConnectionId,
        Guid ReachedPipelineId,
        Guid ReachingConnectionId,
        Guid LooseConnectionId);

    // A state connection with a pipeline drawn from it: Arrival (a start node standing for the connection,
    // out port `subject`, the name the page gives it) → Echo (message→echo, the wire narrowing the subject
    // to its id) → Out. Beside it a second connection reaching another
    // pipeline along the predicate bound to starting one, and a third reaching nothing.
    public static StartFixture StatePipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);
        var standsFor = fx.Playing("standsFor", PipelineRoles.StandsFor);
        var starts = fx.Playing("starts", PipelineRoles.PipelineStart);

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

        var watching = fx.Thing("watchingConn");
        fx.Rel(watching, vocabulary.Is, vocabulary.Connection);

        var arrival = fx.Thing("Arrival");
        fx.Rel(arrival, vocabulary.Is, vocabulary.PipelineInput);
        var subjectPort = fx.Thing("arrival.subject", ("direction", "out"), ("type", "any"), ("portName", PipelineExecutor.SubjectParam));
        fx.Rel(subjectPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(arrival, vocabulary.Has, subjectPort);
        fx.Rel(arrival, standsFor, watching);

        var output = fx.Thing("Out");
        fx.Rel(output, vocabulary.Is, vocabulary.PipelineOutput);
        var resultPort = fx.Thing("out.result", ("direction", "in"), ("type", "string"), ("portName", "result"));
        fx.Rel(resultPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(output, vocabulary.Has, resultPort);

        var drawn = fx.Thing("Drawn from the state");
        fx.Rel(drawn, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(drawn, vocabulary.Has, arrival);
        fx.Rel(drawn, vocabulary.Has, ech);
        fx.Rel(drawn, vocabulary.Has, output);
        fx.Rel(arrival, carries, ech, ("fromPort", PipelineExecutor.SubjectParam), ("toPort", "message"), ("fromPath", "id"));
        fx.Rel(ech, carries, output, ("fromPort", "echo"), ("toPort", "result"));

        var reached = fx.Thing("Reached along the start mark");
        fx.Rel(reached, vocabulary.Is, vocabulary.Pipeline);
        var reaching = fx.Thing("reachingConn");
        fx.Rel(reaching, vocabulary.Is, vocabulary.Connection);
        fx.Rel(reaching, starts, reached);

        var loose = fx.Thing("looseConn");
        fx.Rel(loose, vocabulary.Is, vocabulary.Connection);

        return new StartFixture(fx, drawn.Id, watching.Id, reached.Id, reaching.Id, loose.Id);
    }

    // Field-merge demo: A and B both feed node C's single `in` input, A at to-path `a` and B
    // at to-path `b`, so C receives the two outputs deep-merged into one object. All three nodes share an IO
    // prototype with an `in` input and an `out` output.
    public static (GraphFixture Fixture, Guid PipelineId) FieldMergePipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

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
        fx.Rel(a, carries, c, ("fromPort", "out"), ("toPort", "in"), ("toPath", "a"));
        fx.Rel(b, carries, c, ("fromPort", "out"), ("toPort", "in"), ("toPath", "b"));

        return (fx, pipe.Id);
    }

    // Wire-transform demo: A → C over one wire carrying a JSONata transform
    // that reshapes A's output before it reaches C's `in` input.
    public static (GraphFixture Fixture, Guid PipelineId) WireTransformPipeline(string transform)
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

        var proto = InputOutputPrototype(fx, vocabulary, "IOProto");
        var a = ServiceNode(fx, vocabulary, proto, "A", "a");
        var c = ServiceNode(fx, vocabulary, proto, "C", "c");

        var pipe = fx.Thing("TransformDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, a);
        fx.Rel(pipe, vocabulary.Has, c);
        fx.Rel(a, carries, c, ("fromPort", "out"), ("toPort", "in"), ("transform", transform));

        return (fx, pipe.Id);
    }

    // A single Echo node whose input port `message` is bound to the run param `greeting`
    // (no wires) — exercises run-level param routing.
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

        var ech = fx.Thing("Echo", ("parameterBindings","{\"message\":\"greeting\"}"));
        fx.Rel(ech, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(ech, vocabulary.Has, echConn);

        var pipe = fx.Thing("ParamDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, ech);

        return (fx, pipe.Id);
    }

    // The Water-reserve site-analysis pipeline: a single WaterReserve node whose three inputs
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
            ("parameterBindings","{\"population\":\"population\",\"perCapitaConsumptionM3\":\"perCapitaConsumptionM3\",\"storageCapacityM3\":\"storageCapacityM3\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("SiteAnalysis");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    // The Energy site-analysis pipeline: an EnergyBalance node whose five inputs are param-bound,
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
        NumberPort(fx, vocabulary, proto, "percentOfConsumption", "out");

        var svc = fx.Thing("energySvc");
        fx.Rel(svc, vocabulary.Is, proto);
        var conn = fx.Thing("energyConn", ("Subdomain", "energy-balance"));
        fx.Rel(conn, vocabulary.Is, vocabulary.Connection);
        fx.Rel(conn, vocabulary.Has, svc);

        var node = fx.Thing("EnergyBalance",
            ("parameterBindings","{\"solarPvAreaM2\":\"solarPvAreaM2\",\"solarResourceKwhPerM2PerYear\":\"solarResourceKwhPerM2PerYear\",\"moduleEfficiency\":\"moduleEfficiency\",\"performanceRatio\":\"performanceRatio\",\"otherGenerationMwhPerYear\":\"otherGenerationMwhPerYear\",\"annualConsumptionMwhPerYear\":\"annualConsumptionMwhPerYear\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("EnergyAnalysis");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    // A single Scorer node whose input port `item` is a collection (fan-out) and `weight` is a
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

        var node = fx.Thing("Scorer", ("onItemError", onItemError), ("parameterBindings","{\"item\":\"items\",\"weight\":\"w\"}"));
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, conn);

        var pipe = fx.Thing("FanDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        return (fx, pipe.Id);
    }

    // FanOutPipeline whose prototype also answers `parts`, an out port marked as a collection: each item
    // answers a list under it, and the node gathers them as one flat list.
    public static (GraphFixture Fixture, Guid PipelineId) FanOutWithCollectionOutPipeline(string onItemError = "fail")
    {
        var (fx, pipelineId) = FanOutPipeline(onItemError);
        var parts = fx.Thing("p.parts", ("direction", "out"), ("type", "any"), ("portName", "parts"), ("collection", "true"));
        fx.Rel(parts, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("ScoreProto"), fx.Get("has"), parts);
        return (fx, pipelineId);
    }

    // A two-way decision: Start (out `subject`) → Decide, a node holding its own out ports `accept` and `refuse`
    // beside its service's → Notify from `accept`, Return from `refuse` → End (in `result`) wired from both.
    public static (GraphFixture Fixture, Guid PipelineId) DecisionPipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

        var decideProto = InputOutputPrototype(fx, vocabulary, "DecideProto");
        var echoProto = InputOutputPrototype(fx, vocabulary, "EchoProto");

        var decide = ServiceNode(fx, vocabulary, decideProto, "Decide", "decide");
        var accept = fx.Thing("decide.accept", ("direction", "out"), ("type", "any"), ("portName", "accept"));
        var refuse = fx.Thing("decide.refuse", ("direction", "out"), ("type", "any"), ("portName", "refuse"));
        fx.Rel(accept, vocabulary.Is, vocabulary.Port);
        fx.Rel(refuse, vocabulary.Is, vocabulary.Port);
        fx.Rel(decide, vocabulary.Has, accept);
        fx.Rel(decide, vocabulary.Has, refuse);
        var notify = ServiceNode(fx, vocabulary, echoProto, "Notify", "notify");
        var giveBack = ServiceNode(fx, vocabulary, echoProto, "Return", "return");

        var start = fx.Thing("Start");
        fx.Rel(start, vocabulary.Is, vocabulary.PipelineInput);
        var subjectPort = fx.Thing("start.subject", ("direction", "out"), ("type", "any"), ("portName", "subject"));
        fx.Rel(subjectPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(start, vocabulary.Has, subjectPort);
        var end = fx.Thing("End");
        fx.Rel(end, vocabulary.Is, vocabulary.PipelineOutput);
        var resultPort = fx.Thing("end.result", ("direction", "in"), ("type", "any"), ("portName", "result"));
        fx.Rel(resultPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(end, vocabulary.Has, resultPort);

        var pipe = fx.Thing("DecisionDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        foreach (var node in new[] { start, decide, notify, giveBack, end }) fx.Rel(pipe, vocabulary.Has, node);

        fx.Rel(start, carries, decide, ("fromPort", "subject"), ("toPort", "in"));
        fx.Rel(decide, carries, notify, ("fromPort", "accept"), ("toPort", "in"));
        fx.Rel(decide, carries, giveBack, ("fromPort", "refuse"), ("toPort", "in"));
        fx.Rel(notify, carries, end, ("fromPort", "out"), ("toPort", "result"));
        fx.Rel(giveBack, carries, end, ("fromPort", "out"), ("toPort", "result"));

        return (fx, pipe.Id);
    }

    // One Maker node whose service answers `made`, an out port marked as producing a Thing, beside `note`,
    // which is not. Per item, its `in` port is a collection bound to the run parameter `items`.
    public static (GraphFixture Fixture, Guid PipelineId) ProducingPipeline(bool perItem = false)
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var proto = fx.Thing("MakerProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var portIn = perItem
            ? fx.Thing("m.in", ("direction", "in"), ("type", "any"), ("portName", "in"), ("collection", true))
            : fx.Thing("m.in", ("direction", "in"), ("type", "any"), ("portName", "in"));
        var made = fx.Thing("m.made", ("direction", "out"), ("type", "any"), ("portName", "made"), ("produces", true));
        var note = fx.Thing("m.note", ("direction", "out"), ("type", "any"), ("portName", "note"));
        foreach (var port in new[] { portIn, made, note })
        {
            fx.Rel(port, vocabulary.Is, vocabulary.Port);
            fx.Rel(proto, vocabulary.Has, port);
        }
        var node = ServiceNode(fx, vocabulary, proto, "Maker", "maker", ("parameterBindings", "{\"in\":\"items\"}"));
        var pipe = fx.Thing("MakerDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);
        return (fx, pipe.Id);
    }

    public sealed record ChoiceFixture(GraphFixture Fixture, Guid PipelineId, GraphThing Offers, GraphThing Node, GraphThing[] Offered);

    // One Choose node whose service's in port `strategy` offers three Things along the predicate bound to the
    // choice role; which one the node chose, if any, is the caller's to relate along the same predicate.
    public static ChoiceFixture ChoicePipeline(bool wiredFromASource = false, params (string Name, object Value)[] nodeProperties)
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var offers = fx.Playing("offers", PipelineRoles.Choice);

        var proto = fx.Thing("ChooseProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        var strategy = fx.Thing("p.strategy", ("direction", "in"), ("type", "any"), ("portName", "strategy"));
        var outPort = fx.Thing("p.out", ("direction", "out"), ("type", "any"), ("portName", "out"));
        fx.Rel(strategy, vocabulary.Is, vocabulary.Port);
        fx.Rel(outPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(proto, vocabulary.Has, strategy);
        fx.Rel(proto, vocabulary.Has, outPort);
        var offered = new[] { fx.Thing("Nearest first"), fx.Thing("Cheapest first"), fx.Thing("Soonest first") };
        foreach (var option in offered) fx.Rel(strategy, offers, option);

        var service = fx.Thing("chooseSvc");
        fx.Rel(service, vocabulary.Is, proto);
        var connection = fx.Thing("chooseConn", ("Subdomain", "choose"));
        fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
        fx.Rel(connection, vocabulary.Has, service);
        var node = fx.Thing("Choose", nodeProperties);
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, connection);
        var pipe = fx.Thing("ChoiceDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, node);

        if (wiredFromASource)
        {
            var carries = fx.Thing("carries");
            fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);
            var source = ServiceNode(fx, vocabulary, InputOutputPrototype(fx, vocabulary, "SourceProto"), "Source", "source");
            fx.Rel(pipe, vocabulary.Has, source);
            fx.Rel(source, carries, node, ("fromPort", "out"), ("toPort", "strategy"));
        }

        return new ChoiceFixture(fx, pipe.Id, offers, node, offered);
    }

    // A (out `out`) and B (out `out`) both wired into Gather's collection input `items`, which may also be
    // bound to a run parameter; Gather answers `out`.
    public static (GraphFixture Fixture, Guid PipelineId) GatheredCollectionPipeline(string? boundTo = null)
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

        var sourceProto = InputOutputPrototype(fx, vocabulary, "SourceProto");
        var gatherProto = fx.Thing("GatherProto");
        fx.Rel(gatherProto, vocabulary.Is, vocabulary.Service);
        var items = fx.Thing("g.items", ("direction", "in"), ("type", "any"), ("portName", "items"), ("collection", "true"));
        var outPort = fx.Thing("g.out", ("direction", "out"), ("type", "any"), ("portName", "out"));
        fx.Rel(items, vocabulary.Is, vocabulary.Port);
        fx.Rel(outPort, vocabulary.Is, vocabulary.Port);
        fx.Rel(gatherProto, vocabulary.Has, items);
        fx.Rel(gatherProto, vocabulary.Has, outPort);

        var a = ServiceNode(fx, vocabulary, sourceProto, "A", "a");
        var b = ServiceNode(fx, vocabulary, sourceProto, "B", "b");
        var gather = boundTo is null
            ? ServiceNode(fx, vocabulary, gatherProto, "Gather", "gather")
            : ServiceNode(fx, vocabulary, gatherProto, "Gather", "gather", ("parameterBindings", $"{{\"items\":\"{boundTo}\"}}"));
        var pipe = fx.Thing("GatherDemo");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        foreach (var node in new[] { a, b, gather }) fx.Rel(pipe, vocabulary.Has, node);

        fx.Rel(a, carries, gather, ("fromPort", "out"), ("toPort", "items"));
        fx.Rel(b, carries, gather, ("fromPort", "out"), ("toPort", "items"));

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
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing prototype, string name, string subdomain,
        params (string Name, object Value)[] nodeProperties)
    {
        var service = fx.Thing($"{name}Svc");
        fx.Rel(service, vocabulary.Is, prototype);
        var connection = fx.Thing($"{name}Conn", ("Subdomain", subdomain));
        fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
        fx.Rel(connection, vocabulary.Has, service);
        var node = fx.Thing(name, nodeProperties);
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, connection);
        return node;
    }

    // The same two nodes as DemoPipeline, wired by wire Things rather than by relationships, and wired twice:
    // two Things between one node pair is the case a relationship cannot express, because the model refuses a
    // second relationship on one subject, predicate and target.
    public static (GraphFixture Fixture, Guid PipelineId) ParallelWirePipeline()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var carries = fx.Thing("carries");

        var proto = fx.Thing("RelayProto");
        fx.Rel(proto, vocabulary.Is, vocabulary.Service);
        Port(fx, vocabulary, proto, "message", "in", "string", required: true);
        Port(fx, vocabulary, proto, "context", "in", "string");
        Port(fx, vocabulary, proto, "echo", "out", "string");
        Port(fx, vocabulary, proto, "trace", "out", "string");

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

        WireThing(fx, vocabulary, carries, gen, ech, "echo", "message");
        WireThing(fx, vocabulary, carries, gen, ech, "trace", "context");

        return (fx, pipe.Id);
    }

    private static void WireThing(
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing carries,
        GraphThing from, GraphThing to, string fromPort, string toPort)
    {
        var wire = fx.Thing($"w.{fromPort}.{toPort}", ("fromPort", fromPort), ("toPort", toPort));
        fx.Rel(wire, vocabulary.Is, vocabulary.PipelineWire);
        fx.Rel(from, vocabulary.Has, wire);
        fx.Rel(wire, carries, to);
    }

    private static void NumberPort(
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing prototype, string name, string direction, bool required = false)
        => Port(fx, vocabulary, prototype, name, direction, "number", required);

    private static void Port(
        GraphFixture fx, PipelineVocabulary vocabulary, GraphThing prototype,
        string name, string direction, string type, bool required = false)
    {
        var props = required
            ? new (string, object)[] { ("direction", direction), ("type", type), ("portName", name), ("required", "true") }
            : new (string, object)[] { ("direction", direction), ("type", type), ("portName", name) };
        var port = fx.Thing($"p.{name}", props);
        fx.Rel(port, vocabulary.Is, vocabulary.Port);
        fx.Rel(prototype, vocabulary.Has, port);
    }
}
