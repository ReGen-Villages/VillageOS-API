using FluentAssertions;
using vos.Service.Phloem.Configuration;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// DAG resolution + validation (Feature #5628, #5632): build the executable DAG from the loaded graph and
// validate it acyclic with type-compatible wires.
public class PipelineModelTests
{
    private static readonly PipelineModelOptions Names = new();

    [Fact] // #5873: boundary nodes resolve with a kind + their own declared ports, and no dispatch subdomain.
    public void Build_BoundaryNodes_ResolveKindAndDeclaredPorts()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

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
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ResolvesNodesSubdomainsPortsAndWire()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

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

    [Fact]
    public void Build_NodeWithoutConnection_Throws()
    {
        var fx = new GraphFixture();
        var isP = fx.Thing("is");
        var has = fx.Thing("has");
        var pipelineArch = fx.Thing("Pipeline");
        var nodeArch = fx.Thing("PipelineNode");
        var pipe = fx.Thing("P");
        fx.Rel(pipe, isP, pipelineArch);
        var node = fx.Thing("Lonely");
        fx.Rel(node, isP, nodeArch);
        fx.Rel(pipe, has, node); // node binds no PlatformServiceConnection

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id, Names);

        act.Should().Throw<PipelineModelException>().WithMessage("*binds no PlatformServiceConnection*");
    }

    [Fact]
    public void Validate_DemoPipeline_IsAcyclicAndOrdered()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

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
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

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
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId, Names);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not an input port"));
    }
}
