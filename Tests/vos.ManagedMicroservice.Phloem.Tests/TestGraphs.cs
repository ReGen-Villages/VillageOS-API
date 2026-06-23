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
        var connArch = fx.Thing("Connection");
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
        var connArch = fx.Thing("Connection");
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
}
