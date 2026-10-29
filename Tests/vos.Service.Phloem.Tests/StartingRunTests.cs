using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A follow-on node starts a second run of the pipeline it stands for, about the same subject, carrying what reached
// the node as its parameters, and the first run carries on to its Output node.
public class StartingRunTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    private static (TestGraphs.StartingFixture Starting, FakeGateway Gateway, PipelineExecutor Executor) Arrange(string? transformIntoTheFollowOn = null)
    {
        var starting = TestGraphs.StartingPipeline(transformIntoTheFollowOn: transformIntoTheFollowOn);
        var gateway = new FakeGateway(starting.Fixture.Build())
        {
            OnCall = (subdomain, envelope) => subdomain == "ech"
                ? NodeOk(("echo", envelope.GetProperty("inputs").GetProperty("message").GetString()!))
                : NodeFail($"unexpected {subdomain}"),
        };
        return (starting, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    [Fact]
    public async Task A_follow_on_node_leaves_a_second_run_about_the_same_subject_carrying_what_reached_it_and_naming_the_first()
    {
        var (starting, gateway, executor) = Arrange();

        var first = await executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None, subject: Plot);

        first.Success.Should().BeTrue();
        await Settle.UntilAsync(() => { lock (gateway.RunsCreated) return gateway.RunsCreated.Count == 2; }, "the second run is recorded");
        var second = gateway.RunsCreated.Last();
        second.PipelineId.Should().Be(starting.NextPipelineId);
        second.StartedBy.Should().Be(first.RunId);
        second.Subject.Should().Be(Plot);
        var parameters = gateway.Runs[second.RunId].Parameters;
        parameters.EnumerateObject().Select(parameter => parameter.Name).Should().Equal("subject", "result");
        parameters.GetProperty("result").GetString().Should().Be("world");
        var followOn = first.Nodes.Single(node => node.Name == TestGraphs.FollowOnNode);
        followOn.Status.Should().Be(RunStatus.Succeeded);
        followOn.Outputs[ModelNames.StartedRun].GetString().Should().Be(second.RunId.ToString());
        first.Result!.Value.GetProperty("result").GetString().Should().Be("world", "the follow-on node passes on what reached it");
    }

    [Fact]
    public async Task A_follow_on_node_whose_inputs_cannot_be_assembled_fails_naming_the_pipeline_it_would_have_started_and_starts_nothing()
    {
        var (starting, gateway, executor) = Arrange(transformIntoTheFollowOn: "$error(\"the wire refuses\")");

        var result = await executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Nodes.Single(node => node.Name == TestGraphs.FollowOnNode).Error
            .Should().StartWith($"'What follows' could not be started from '{TestGraphs.FollowOnNode}':");
        gateway.RunsCreated.Should().ContainSingle();
    }

    [Fact]
    public async Task A_run_no_follow_on_node_started_names_no_starting_run()
    {
        var (starting, gateway, executor) = Arrange();

        await executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        gateway.RunsCreated.First().StartedBy.Should().BeNull();
    }

    private static NodeCallResult NodeOk(params (string Port, string Value)[] outputs) =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = outputs.ToDictionary(o => o.Port, o => o.Value) }));

    private static NodeCallResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { result = "fail", error }));
}
