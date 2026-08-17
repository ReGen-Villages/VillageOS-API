using FluentAssertions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// DAG resolution + validation (Feature #5628, #5632): build the executable DAG from the loaded graph and
// validate it acyclic with type-compatible wires.
public class PipelineModelTests
{
    [Fact] // #5873: boundary nodes resolve with a kind + their own declared ports, and no dispatch subdomain.
    public void Build_BoundaryNodes_ResolveKindAndDeclaredPorts()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var input = dag.Node(fx.Get("In").Id)!;
        input.Kind.Should().Be(DagNodeKind.Input);
        input.Subdomain.Should().BeEmpty();
        input.OutputPorts.Select(p => p.PortName).Should().Contain("seed");

        var output = dag.Node(fx.Get("Out").Id)!;
        output.Kind.Should().Be(DagNodeKind.Output);
        output.Subdomain.Should().BeEmpty();
        output.InputPorts.Select(p => p.PortName).Should().Contain("result");

        // The service node in the same pipeline still resolves as a Service with a subdomain.
        dag.Node(fx.Get("Echo").Id)!.Kind.Should().Be(DagNodeKind.Service);

        // Both boundary wires are present (In.seed → Echo.message, Echo.echo → Out.result).
        dag.Wires.Should().HaveCount(2);
    }

    [Fact] // A boundary pipeline validates: every wire connects real ports and the graph is acyclic.
    public void Validate_BoundaryPipeline_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ResolvesNodesSubdomainsPortsAndWire()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Nodes.Should().HaveCount(2);
        var generate = dag.Node(fx.Get("Generate").Id)!;
        var echo = dag.Node(fx.Get("Echo").Id)!;
        generate.Subdomain.Should().Be("gen");
        echo.Subdomain.Should().Be("ech");

        // ports resolve from the bound service's is-chain (proto)
        generate.Port("echo")!.IsOutput.Should().BeTrue();
        echo.Port("message")!.IsInput.Should().BeTrue();
        echo.Port("message")!.Required.Should().BeTrue();

        dag.Wires.Should().ContainSingle();
        var wire = dag.Wires[0];
        wire.FromNodeId.Should().Be(generate.NodeId);
        wire.ToNodeId.Should().Be(echo.NodeId);
        wire.FromPort.Should().Be("echo");
        wire.ToPort.Should().Be("message");
    }

    // The whole point of the flags (#6516): a model may call its archetypes anything, and a deployment that
    // renames them used to leave this orchestrator starting cleanly and finding nothing.
    [Fact]
    public void Build_WhenTheModelNamesEveryArchetypeSomethingElse_ResolvesTheSameDag()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary(namePrefix: "Renamed");
        var feeds = fx.Thing("carries");
        fx.Rel(feeds, vocabulary.Is, vocabulary.PipelineWire);

        var prototype = fx.Thing("proto");
        fx.Rel(prototype, vocabulary.Is, vocabulary.Service);
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "produced"));
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "consumed"));
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(prototype, vocabulary.Has, portOut);
        fx.Rel(prototype, vocabulary.Has, portIn);

        GraphThing Node(string name, string subdomain)
        {
            var service = fx.Thing($"{name}Service");
            fx.Rel(service, vocabulary.Is, prototype);
            var connection = fx.Thing($"{name}Connection", ("Subdomain", subdomain));
            fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
            fx.Rel(connection, vocabulary.Has, service);
            var node = fx.Thing(name);
            fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
            fx.Rel(node, vocabulary.Has, connection);
            return node;
        }

        var first = Node("First", "one");
        var second = Node("Second", "two");
        var pipe = fx.Thing("Renamed pipeline");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Rel(pipe, vocabulary.Has, first);
        fx.Rel(pipe, vocabulary.Has, second);
        fx.Rel(first, feeds, second, ("fromPort", "produced"), ("toPort", "consumed"));

        var dag = PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        dag.Nodes.Should().HaveCount(2);
        dag.Node(first.Id)!.Subdomain.Should().Be("one");
        dag.Wires.Should().ContainSingle();
        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    // A model that marks nothing answers "no" to every question the orchestrator asks of it, so a pipeline
    // fully described in the model reads as one with nothing in it.
    [Fact]
    public void Build_WhenTheModelMarksNoArchetypes_SaysWhichRolesAreUnmarked()
    {
        var fx = new GraphFixture();
        var isPredicate = fx.Thing("is");
        var pipelineArchetype = fx.Thing("Pipeline");
        var pipe = fx.Thing("P");
        fx.Rel(pipe, isPredicate, pipelineArchetype);

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>()
            .WithMessage("*The model marks no archetype for*")
            .And.Message.Should().Contain(PipelineArchetypes.PipelineFlag)
            .And.Contain(PipelineArchetypes.PortFlag);
    }

    // One role left unmarked is refused by name rather than being read as "no node plays that role", which
    // is the silence this replaced.
    [Fact]
    public void Build_WhenOneRoleIsUnmarked_NamesOnlyThatRole()
    {
        var fx = new GraphFixture();
        var isPredicate = fx.Thing("is");
        // Named after the flag each carries, because in this test the name is the one thing that cannot matter.
        foreach (var roleFlag in PipelineArchetypes.DagRoleFlags.Where(f => f != PipelineArchetypes.PipelineWireFlag))
            fx.Archetype(roleFlag, roleFlag);
        var pipe = fx.Thing("P");
        fx.Rel(pipe, isPredicate, fx.Get(PipelineArchetypes.PipelineFlag));

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>()
            .WithMessage($"*{PipelineArchetypes.PipelineWireFlag}*")
            .And.Message.Should().NotContain(PipelineArchetypes.PortFlag);
    }

    [Fact]
    public void Build_NodeWithoutConnection_Throws()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var pipe = fx.Thing("P");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        var node = fx.Thing("Lonely");
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(pipe, vocabulary.Has, node); // node binds no connection

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*binds no service connection*");
    }

    [Fact]
    public void Validate_DemoPipeline_IsAcyclicAndOrdered()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeTrue();
        var order = result.Order.ToList();
        order.IndexOf(fx.Get("Generate").Id).Should().BeLessThan(order.IndexOf(fx.Get("Echo").Id));
    }

    [Fact]
    public void Validate_Cycle_IsRejected()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        // add the back-edge Echo –feeds(echo→message)→ Generate to make a cycle
        fx.Rel(fx.Get("Echo"), fx.Get("feeds"), fx.Get("Generate"), ("fromPort", "echo"), ("toPort", "message"));
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cycle"));
    }

    [Fact]
    public void Validate_WireToUnknownPort_IsRejected()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        // a wire whose target port does not exist on Echo
        fx.Rel(fx.Get("Generate"), fx.Get("feeds"), fx.Get("Echo"), ("fromPort", "echo"), ("toPort", "nope"));
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not an input port"));
    }
}
