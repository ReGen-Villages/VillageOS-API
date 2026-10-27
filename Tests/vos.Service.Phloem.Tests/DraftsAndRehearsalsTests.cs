using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class DraftsAndRehearsalsTests
{
    private static readonly JsonElement Seed = JsonSerializer.SerializeToElement(new { seed = "world" });

    private static NodeCallResult Answer(string port, string value) =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = new Dictionary<string, string> { [port] = value } }));

    private static (FakeGateway Gateway, PipelineExecutor Executor) Arrange(GraphFixture fixture, Func<string, JsonElement, NodeCallResult>? answer = null)
    {
        var gateway = new FakeGateway(fixture.Build()) { OnCall = answer ?? ((_, _) => Answer("echo", "heard")) };
        return (gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static void EveryServiceRehearses(GraphFixture fixture) => fixture.State(fixture.Get("Service"), ModelNames.Rehearses, true);

    private static void Unpublish(GraphFixture fixture, string pipelineName) => fixture.State(fixture.Get(pipelineName), ModelNames.Published, false);

    [Fact]
    public async Task A_draft_run_for_real_is_refused_and_runs_nothing()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        Unpublish(fx, "BoundaryDemo");
        var (gateway, executor) = Arrange(fx);

        var result = await executor.RunAsync(pipelineId, Seed, CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().BeEmpty();
        gateway.StatusUpdates.Should().Equal(RunStatus.Refused);
        gateway.RunError.Should().Contain("'BoundaryDemo' is a draft");
    }

    [Fact]
    public async Task A_rehearsal_of_a_draft_calls_each_service_that_rehearses_telling_it_so_and_ends_rehearsed()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        Unpublish(fx, "BoundaryDemo");
        EveryServiceRehearses(fx);
        var (gateway, executor) = Arrange(fx);

        var result = await executor.RunAsync(pipelineId, Seed, CancellationToken.None, rehearsal: true);

        result.Success.Should().BeTrue();
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Rehearsed);
        gateway.Envelopes.Should().ContainSingle().Which.Envelope.GetProperty(ModelNames.Rehearsal).GetBoolean().Should().BeTrue();
        gateway.Rehearsals.Should().Contain(result.RunId);
    }

    [Fact]
    public async Task A_run_for_real_tells_its_blocks_nothing_of_rehearsals()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        EveryServiceRehearses(fx);
        var (gateway, executor) = Arrange(fx);

        await executor.RunAsync(pipelineId, Seed, CancellationToken.None);

        gateway.Envelopes.Single().Envelope.TryGetProperty(ModelNames.Rehearsal, out _).Should().BeFalse();
        gateway.Rehearsals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rehearsal_stops_at_a_service_that_does_not_rehearse_and_calls_nothing()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var (gateway, executor) = Arrange(fx);

        var result = await executor.RunAsync(pipelineId, Seed, CancellationToken.None, rehearsal: true);

        result.Success.Should().BeFalse();
        gateway.Called.Should().BeEmpty();
        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.CannotRehearse);
        result.Nodes.Single(node => node.Name == "Echo").Error.Should().Contain("does not rehearse");
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Skipped);
        gateway.StatusUpdates.Last().Should().Be(RunStatus.CannotRehearse);
    }

    [Fact]
    public async Task A_rehearsal_reaching_an_end_that_tells_a_system_sends_nothing_and_keeps_what_it_would_have_sent()
    {
        var telling = TestGraphs.TellingPipeline();
        EveryServiceRehearses(telling.Fixture);
        var (gateway, executor) = Arrange(telling.Fixture);

        var result = await executor.RunAsync(telling.PipelineId, Seed, CancellationToken.None, rehearsal: true);

        gateway.MessagesSent.Should().BeEmpty();
        var end = result.Nodes.Single(node => node.Name == "Out");
        end.Status.Should().Be(RunStatus.Rehearsed);
        end.Outputs["result"].GetString().Should().Be("heard");
        result.Success.Should().BeTrue();
        result.Result!.Value.GetProperty("result").GetString().Should().Be("heard", "the run's result is what reached its end, rehearsed or not");
        gateway.RunResult!.Value.GetProperty("result").GetString().Should().Be("heard");
    }

    [Fact]
    public async Task A_rehearsal_reaching_an_end_that_starts_a_pipeline_starts_nothing()
    {
        var starting = TestGraphs.StartingPipeline();
        EveryServiceRehearses(starting.Fixture);
        var (gateway, executor) = Arrange(starting.Fixture);

        var result = await executor.RunAsync(starting.PipelineId, Seed, CancellationToken.None, rehearsal: true);

        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Rehearsed);
        gateway.RunsCreated.Should().ContainSingle("only the rehearsal itself is recorded");
    }

    [Fact]
    public async Task A_rehearsed_end_whose_inputs_cannot_be_put_together_fails_saying_so()
    {
        var starting = TestGraphs.StartingPipeline(transformIntoTheEnd: "$error(\"no shape for the end\")");
        EveryServiceRehearses(starting.Fixture);
        var (_, executor) = Arrange(starting.Fixture);

        var result = await executor.RunAsync(starting.PipelineId, Seed, CancellationToken.None, rehearsal: true);

        var end = result.Nodes.Single(node => node.Name == "Out");
        end.Status.Should().Be(RunStatus.Failed);
        end.Error.Should().Contain("could not be put together").And.Contain("no shape for the end");
    }

    [Fact]
    public async Task A_rehearsal_stops_at_a_waiting_node_and_places_no_vigil()
    {
        var waiting = TestGraphs.WaitingPipeline();
        EveryServiceRehearses(waiting.Fixture);
        var (gateway, executor) = Arrange(waiting.Fixture, (_, _) => Answer("notice", "sent for review"));

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: new RunSubject(Guid.NewGuid(), "Plot 7"), rehearsal: true);

        var review = result.Nodes.Single(node => node.NodeId == waiting.Review.Id);
        review.Status.Should().Be(RunStatus.CannotRehearse);
        review.Error.Should().Contain("'decided'");
        gateway.VigilsPlaced.Should().BeEmpty();
        gateway.Called.Should().NotContain("review");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rehearsal_relates_nothing_a_block_answered_as_produced(bool perItem)
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline(perItem);
        EveryServiceRehearses(fx);
        var made = Guid.NewGuid();
        var (gateway, executor) = Arrange(fx, (_, _) => new NodeCallResult(200,
            JsonSerializer.Serialize(new { result = "success", outputs = new { made = new { id = made, name = "a Thing" } } })));

        await executor.RunAsync(pipelineId, perItem ? JsonSerializer.SerializeToElement(new { items = new[] { "x", "y" } }) : default,
            CancellationToken.None, rehearsal: true);

        gateway.Called.Should().NotBeEmpty();
        gateway.Produced.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_item_of_a_rehearsed_fan_out_is_told_it_is_a_rehearsal()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline();
        EveryServiceRehearses(fx);
        var (gateway, executor) = Arrange(fx, (_, _) => Answer("score", "1"));

        await executor.RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { items = new[] { "a", "b" }, w = 1 }), CancellationToken.None, rehearsal: true);

        gateway.Envelopes.Should().HaveCount(2).And.OnlyContain(sent => sent.Envelope.GetProperty(ModelNames.Rehearsal).GetBoolean());
    }

    [Fact]
    public void A_service_rehearses_when_it_or_a_kind_above_it_says_so()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.State(fx.Get("EchoProto"), ModelNames.Rehearses, true);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Node(fx.Get("Echo").Id)!.Rehearses.Should().BeTrue();
        dag.Node(fx.Get("Generate").Id)!.Rehearses.Should().BeFalse();
        dag.Published.Should().BeTrue();
    }
}
