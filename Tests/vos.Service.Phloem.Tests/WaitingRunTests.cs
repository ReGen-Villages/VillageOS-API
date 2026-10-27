using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A run pauses at a node that waits for a Thing to enter a state, and resumes from its record in the model
// when the vigil placed for it is answered.
public class WaitingRunTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    private static (TestGraphs.WaitingFixture Waiting, FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        TestGraphs.WaitingFixture? fixture = null)
    {
        var waiting = fixture ?? TestGraphs.WaitingPipeline();
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            OnCall = (subdomain, envelope) => subdomain switch
            {
                "review" => NodeOk(("notice", "sent for review")),
                "publish" => NodeOk(("out", envelope.GetProperty("inputs").GetProperty("in").GetString()!)),
                _ => NodeFail($"unexpected {subdomain}"),
            },
        };
        return (waiting, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static async Task<(Guid RunId, Guid VigilId)> WaitAsync(TestGraphs.WaitingFixture waiting, FakeGateway gateway, PipelineExecutor executor)
    {
        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        return (result.RunId, gateway.HeldVigils[(result.RunId, waiting.Review.Id)]);
    }

    [Fact]
    public async Task A_run_calls_the_waiting_node_then_waits_for_its_Thing_to_enter_the_state_and_calls_nothing_after_it()
    {
        var (waiting, gateway, executor) = Arrange();

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("The run waits for 'Plot 7' to enter 'decided' at 'Review'.");
        gateway.Called.Should().Equal("review");
        gateway.Records[(result.RunId, waiting.Review.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Records[(result.RunId, waiting.Review.Id)].Outputs["notice"].GetString().Should().Be("sent for review");
        gateway.Awaited[(result.RunId, waiting.Review.Id)].Should().Be(Plot.Id);
        var vigil = gateway.VigilsPlaced.Should().ContainSingle().Subject;
        (vigil.SubjectId, vigil.RangeId).Should().Be((Plot.Id, waiting.Decided.Id));
        gateway.HeldVigils[(result.RunId, waiting.Review.Id)].Should().Be(vigil.VigilId);
        gateway.Records.Should().NotContainKey((result.RunId, waiting.Publish.Id));
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Waiting);
    }

    [Fact]
    public async Task The_vigils_answer_resumes_the_run_from_its_record_without_calling_the_waiting_node_again()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);

        var outcome = await executor.ResumeAsync(vigilId, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review", "publish");
        gateway.Envelopes.Last().Envelope.GetProperty("inputs").GetProperty("in").GetString().Should().Be("sent for review");
        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.Records[(runId, waiting.Publish.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.Awaited.Should().NotContainKey((runId, waiting.Review.Id));
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_resumed_run_reads_its_parameters_back_from_the_record()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        var resumed = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await resumed.ResumeAsync(vigilId, CancellationToken.None);

        gateway.Runs[runId].Parameters.GetProperty(PipelineExecutor.SubjectParam).GetProperty("name").GetString().Should().Be("Plot 7");
        gateway.Records[(runId, waiting.Publish.Id)].Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task Waits_in_parallel_branches_each_hold_a_vigil_and_the_run_ends_only_when_both_are_answered()
    {
        var (waiting, gateway, executor) = Arrange(TestGraphs.WaitingPipeline(approvedBeside: true));
        gateway.OnCall = (subdomain, envelope) => subdomain switch
        {
            "review" or "approve" => NodeOk(("notice", $"sent to {subdomain}")),
            "publish" => NodeOk(("out", envelope.GetProperty("inputs").GetProperty("in").GetString()!)),
            _ => NodeFail($"unexpected {subdomain}"),
        };
        var approve = waiting.Fixture.Get("Approve");

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        gateway.VigilsPlaced.Select(vigil => vigil.RangeId).Should().BeEquivalentTo([waiting.Decided.Id, waiting.Fixture.Get("approved").Id]);

        await executor.ResumeAsync(gateway.HeldVigils[(result.RunId, waiting.Review.Id)], CancellationToken.None);
        gateway.Called.Should().Contain("publish");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Waiting);

        await executor.ResumeAsync(gateway.HeldVigils[(result.RunId, approve.Id)], CancellationToken.None);
        gateway.Records[(result.RunId, approve.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Succeeded);
        gateway.VigilsPlaced.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_second_answer_to_the_same_vigil_resumes_nothing()
    {
        var (waiting, gateway, executor) = Arrange();
        var (_, vigilId) = await WaitAsync(waiting, gateway, executor);
        await executor.ResumeAsync(vigilId, CancellationToken.None);

        var again = await executor.ResumeAsync(vigilId, CancellationToken.None);

        again.Should().Be(ResumeOutcome.AlreadyResumed);
        gateway.Called.Should().Equal("review", "publish");
    }

    [Fact]
    public async Task Two_answers_arriving_together_resume_the_run_once()
    {
        var (waiting, gateway, executor) = Arrange();
        var (_, vigilId) = await WaitAsync(waiting, gateway, executor);

        var outcomes = await Task.WhenAll(executor.ResumeAsync(vigilId, CancellationToken.None), executor.ResumeAsync(vigilId, CancellationToken.None));

        outcomes.Should().BeEquivalentTo([ResumeOutcome.Resumed, ResumeOutcome.AlreadyResumed]);
        gateway.Called.Should().Equal("review", "publish");
    }

    // The platform answers at once for a Thing already in the state, which can be before the node run is related
    // to its vigil.
    [Fact]
    public async Task An_answer_arriving_while_the_vigil_is_being_placed_finds_the_node_run_once_it_holds_the_vigil()
    {
        var (waiting, gateway, executor) = Arrange();
        Task<ResumeOutcome>? answer = null;
        gateway.OnVigilPlaced = vigilId => answer = executor.ResumeAsync(vigilId, CancellationToken.None);

        var paused = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        paused.Error.Should().Be("The run waits for 'Plot 7' to enter 'decided' at 'Review'.");
        await Settle.ForAsync(answer!, "the answer that arrived during placement resumes the run");
        (await answer!).Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review", "publish");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_vigil_no_node_run_holds_resumes_nothing()
    {
        var (_, gateway, executor) = Arrange();

        var outcome = await executor.ResumeAsync(Guid.NewGuid(), CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.NotHeld);
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_record_missing_on_resume_fails_the_run_naming_the_node_and_calls_nothing_again()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        var start = waiting.Fixture.Get("A submission arrives");
        gateway.Records.Remove((runId, start.Id));

        var outcome = await executor.ResumeAsync(vigilId, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("The record of 'A submission arrives' is missing, so the run cannot resume without running it again.");
    }

    [Fact]
    public async Task A_record_a_best_effort_write_left_missing_is_written_again_before_the_vigil()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var (_, gateway, executor) = Arrange(waiting);
        var refusedOnce = false;
        gateway.RefuseNodeRunWrite = (nodeId, status) =>
            nodeId == waiting.Review.Id && status == RunStatus.Waiting && !refusedOnce && (refusedOnce = true);

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        gateway.Records[(result.RunId, waiting.Review.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.VigilsPlaced.Should().ContainSingle();
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_fails_the_node_and_places_no_vigil()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var (_, gateway, executor) = Arrange(waiting);
        gateway.RefuseNodeRunWrite = (nodeId, status) => nodeId == waiting.Review.Id && status == RunStatus.Waiting;

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        gateway.VigilsPlaced.Should().BeEmpty();
        var review = result.Nodes.Single(node => node.Name == "Review");
        review.Status.Should().Be(RunStatus.Failed);
        review.Error.Should().Be("The record of 'Review' could not be written to the model, so the run could not resume from it.");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_waiting_node_whose_port_holds_no_Thing_fails_without_being_called()
    {
        var (waiting, gateway, executor) = Arrange();

        var result = await executor.RunAsync(waiting.PipelineId, JsonSerializer.SerializeToElement(new { subject = "not a Thing" }), CancellationToken.None);

        gateway.Called.Should().BeEmpty();
        result.Nodes.Single(node => node.Name == "Review").Error
            .Should().Be("'Review' waits for the Thing reaching its port 'submission', but what reached it names no Thing.");
        gateway.VigilsPlaced.Should().BeEmpty();
    }

    [Fact]
    public async Task A_vigil_the_platform_refuses_fails_the_node_with_its_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        gateway.PlacementRefusal = "Range 'decided' does not judge 'Plot 7'";

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        var review = result.Nodes.Single(node => node.Name == "Review");
        review.Status.Should().Be(RunStatus.Failed);
        review.Error.Should().Be("'Review' cannot wait: the platform refused its vigil: Range 'decided' does not judge 'Plot 7'");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_restarted_orchestrator_places_a_vigil_again_for_a_wait_whose_vigil_is_gone_and_leaves_a_live_one_alone()
    {
        var (waiting, gateway, executor) = Arrange();
        var (gone, goneVigil) = await WaitAsync(waiting, gateway, executor);
        var (kept, keptVigil) = await WaitAsync(waiting, gateway, executor);
        gateway.LiveVigils.Remove(goneVigil);
        var restarted = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var placedAgain = await restarted.RecoverWaitsAsync(CancellationToken.None);

        placedAgain.Should().Be(1);
        var replacement = gateway.VigilsPlaced.Last();
        (replacement.SubjectId, replacement.RangeId).Should().Be((Plot.Id, waiting.Decided.Id));
        gateway.HeldVigils[(gone, waiting.Review.Id)].Should().Be(replacement.VigilId);
        gateway.HeldVigils[(kept, waiting.Review.Id)].Should().Be(keptVigil);
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_Thing_is_no_longer_named()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        gateway.LiveVigils.Remove(vigilId);
        gateway.Awaited.Remove((runId, waiting.Review.Id));

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' waited for a Thing the model no longer names, so nothing could resume it.");
    }

    [Fact]
    public async Task A_pipeline_that_cannot_be_loaded_when_its_vigil_is_answered_fails_the_run_with_the_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        var (_, vigilId) = await WaitAsync(waiting, gateway, executor);
        gateway.LoadFailure = new PipelineModelException("Node 'Review' waits for two states, so no one answer could resume it.");

        var outcome = await executor.ResumeAsync(vigilId, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("Node 'Review' waits for two states, so no one answer could resume it.");
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_node_no_longer_waits()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        gateway.LiveVigils.Remove(vigilId);
        waiting.Fixture.Unrelate(waiting.Review, waiting.ProvenBy, waiting.Decided);
        waiting.Fixture.Unrelate(waiting.Review, waiting.AwaitsThrough, waiting.Submission);
        gateway.Graph = waiting.Fixture.Build();

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' no longer waits for a state, so nothing could resume it.");
        gateway.VigilsPlaced.Should().ContainSingle();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_pipeline_no_longer_loads_with_the_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        gateway.LiveVigils.Remove(vigilId);
        gateway.LoadFailure = new PipelineModelException("Node 'Review' chose two Things for its port 'submission'.");

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("Node 'Review' chose two Things for its port 'submission'.");
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_vigil_the_platform_refuses_to_place_again()
    {
        var (waiting, gateway, executor) = Arrange();
        var (runId, vigilId) = await WaitAsync(waiting, gateway, executor);
        gateway.LiveVigils.Remove(vigilId);
        gateway.PlacementRefusal = "Range 'decided' does not judge 'Plot 7'";

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' cannot wait: the platform refused its vigil: Range 'decided' does not judge 'Plot 7'");
    }

    [Fact]
    public async Task A_vigil_whose_placing_throws_fails_the_node_with_the_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        gateway.PlacementFailure = new HttpRequestException("the broker is away");

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        var review = result.Nodes.Single(node => node.Name == "Review");
        review.Status.Should().Be(RunStatus.Failed);
        review.Error.Should().Be("'Review' cannot wait: its vigil could not be placed: the broker is away");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_record_that_cannot_be_read_back_is_written_again_in_full_and_then_fails_the_waiting_node_rather_than_placing_a_vigil()
    {
        var (waiting, gateway, executor) = Arrange();
        gateway.RecordReadFailure = new HttpRequestException("the broker is away");

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        gateway.NodeRunsPersisted.Where(node => node.Name == "Review" && node.Status == RunStatus.Waiting).Should().HaveCount(2);
        result.Nodes.Single(node => node.Name == "Review").Error
            .Should().Be("The record of 'Review' could not be written to the model, so the run could not resume from it.");
        gateway.VigilsPlaced.Should().BeEmpty();
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    private static NodeCallResult NodeOk(params (string Port, string Value)[] outputs) =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = outputs.ToDictionary(o => o.Port, o => o.Value) }));

    private static NodeCallResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { result = "fail", error }));
}
