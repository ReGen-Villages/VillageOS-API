using FluentAssertions;
using vos.ManagedMicroservice.Phloem.Execution;
using vos.ManagedMicroservice.Phloem.Model;
using Xunit;

namespace vos.ManagedMicroservice.Phloem.Tests;

// DAG resolution + validation (Feature #5628, #5632): build the executable DAG from the loaded graph and
// validate it acyclic with type-compatible wires.
public class PipelineModelTests
{
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
        fx.Rel(pipe, has, node); // node binds no Connection

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*binds no Connection*");
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
