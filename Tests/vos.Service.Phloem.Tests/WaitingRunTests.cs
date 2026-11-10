using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A run pauses at a node that waits for a Thing to enter a state, and resumes from its record in the model
// when the platform answers the node run's watch.
public class WaitingRunTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    private static (TestGraphs.WaitingFixture Waiting, FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        TestGraphs.WaitingFixture? fixture = null, ILogger<PipelineExecutor>? log = null)
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
        return (waiting, gateway, new PipelineExecutor(gateway, log ?? NullLogger<PipelineExecutor>.Instance, new ModelClock()));
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
        gateway.Watches[(result.RunId, waiting.Review.Id)].Should().Be(new Watch(Plot.Id, waiting.Decided.Id));
        gateway.Records.Should().NotContainKey((result.RunId, waiting.Publish.Id));
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Waiting);
    }

    [Theory]
    [InlineData(false, "\"kept a\"")]
    [InlineData(true, "[\"kept a\"]")]
    public async Task A_resumed_run_carries_on_along_the_decision_a_node_made_before_the_wait(bool sortsPerItem, string sorted)
    {
        var waiting = TestGraphs.WaitingPipeline(sortedBeside: true, sortsPerItem: sortsPerItem);
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            OnCall = (subdomain, envelope) => subdomain switch
            {
                "review" => NodeOk(("notice", "sent for review")),
                "sort" => NodeOk(("out", "kept " + envelope.GetProperty("inputs").GetProperty("in").GetString())),
                "publish" => new NodeCallResult(200, JsonSerializer.Serialize(new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
                _ => NodeFail($"unexpected {subdomain}"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var items = sortsPerItem ? JsonSerializer.SerializeToElement(new { items = new[] { "a" } }) : JsonSerializer.SerializeToElement(new { items = "a" });
        var waited = await executor.RunAsync(waiting.PipelineId, items, CancellationToken.None, subject: Plot);

        await executor.ResumeAsync(waited.RunId, waiting.Review.Id, CancellationToken.None);

        var published = gateway.Envelopes.Last(sent => sent.Subdomain == "publish").Envelope.GetProperty("inputs").GetProperty("in");
        published.GetProperty("notice").GetString().Should().Be("sent for review");
        published.GetProperty("sorted").GetRawText().Should().Be(sorted);
    }

    [Fact]
    public async Task A_node_that_runs_once_per_item_judges_each_items_choice_by_that_items_own_inputs()
    {
        var waiting = TestGraphs.WaitingPipeline(sortedBeside: true, sortsPerItem: true, keepWhen: "inputs.in = 'a'");
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            OnCall = (subdomain, envelope) => subdomain switch
            {
                "review" => NodeOk(("notice", "sent for review")),
                "sort" => NodeOk(("out", "sorted " + envelope.GetProperty("inputs").GetProperty("in").GetString())),
                "publish" or "discard" => new NodeCallResult(200, JsonSerializer.Serialize(new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
                _ => NodeFail($"unexpected {subdomain}"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var waited = await executor.RunAsync(waiting.PipelineId, JsonSerializer.SerializeToElement(new { items = new[] { "a", "b" } }), CancellationToken.None, subject: Plot);

        await executor.ResumeAsync(waited.RunId, waiting.Review.Id, CancellationToken.None);

        JsonElement InputTo(string subdomain) => gateway.Envelopes.Last(sent => sent.Subdomain == subdomain).Envelope.GetProperty("inputs").GetProperty("in");
        InputTo("publish").GetProperty("sorted").GetRawText().Should().Be("[\"sorted a\"]");
        InputTo("discard").GetRawText().Should().Be("[\"sorted b\"]");
    }

    [Fact]
    public async Task Items_read_back_from_the_record_keep_their_numbers_on_a_node_that_runs_once_the_run_resumes()
    {
        var waiting = TestGraphs.WaitingPipeline(sortedBeside: true, sortsPerItem: true, keepWhen: "inputs.in = 'b'", recordedPerItem: true);
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            OnCall = (subdomain, envelope) => subdomain switch
            {
                "review" => NodeOk(("notice", "sent for review")),
                "sort" => NodeOk(("out", "sorted " + envelope.GetProperty("inputs").GetProperty("in").GetString())),
                _ => new NodeCallResult(200, JsonSerializer.Serialize(new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var waited = await executor.RunAsync(waiting.PipelineId, JsonSerializer.SerializeToElement(new { items = new[] { "a", "b" } }), CancellationToken.None, subject: Plot);
        var recordedBeforeResuming = gateway.Called.Contains("record");

        await executor.ResumeAsync(waited.RunId, waiting.Review.Id, CancellationToken.None);

        recordedBeforeResuming.Should().BeFalse();
        var recorded = gateway.Envelopes.Single(sent => sent.Subdomain == "record").Envelope;
        (recorded.GetProperty("inputs").GetProperty("in").GetString(), recorded.GetProperty("index").GetInt32()).Should().Be(("sorted b", 1));
        gateway.ItemRecordsOf("Record").Should().Equal((1, 2));
    }

    [Fact]
    public async Task An_answer_resumes_the_run_from_its_record_without_calling_the_waiting_node_again()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);

        var outcome = await executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review", "publish");
        gateway.Envelopes.Last().Envelope.GetProperty("inputs").GetProperty("in").GetString().Should().Be("sent for review");
        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.Records[(runId, waiting.Publish.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.Watches.Should().NotContainKey((runId, waiting.Review.Id));
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_resumed_run_reads_its_parameters_back_from_the_record()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        var resumed = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await resumed.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        gateway.Runs[runId].Parameters.GetProperty(PipelineExecutor.SubjectParam).GetProperty("name").GetString().Should().Be("Plot 7");
        gateway.Records[(runId, waiting.Publish.Id)].Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task Waits_in_parallel_branches_each_hold_a_watch_and_the_run_ends_only_when_both_are_answered()
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
        gateway.Watches.Values.Select(watch => watch.RangeId).Should().BeEquivalentTo([waiting.Decided.Id, waiting.Fixture.Get("approved").Id]);

        await executor.ResumeAsync(result.RunId, waiting.Review.Id, CancellationToken.None);
        gateway.Called.Should().Contain("publish");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Waiting);

        await executor.ResumeAsync(result.RunId, approve.Id, CancellationToken.None);
        gateway.Records[(result.RunId, approve.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Succeeded);
        gateway.WatchWrites.Where(write => write.Status == RunStatus.Waiting).Should().HaveCount(2);
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task Resuming_a_wait_already_resumed_finds_nothing_waiting()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        await executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        var again = await executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        again.Should().Be(ResumeOutcome.NothingWaiting);
        gateway.Called.Should().Equal("review", "publish");
    }

    [Fact]
    public async Task Two_answers_arriving_together_resume_the_run_once()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);

        var outcomes = await Task.WhenAll(executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None),
            executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None));

        outcomes.Should().BeEquivalentTo([ResumeOutcome.Resumed, ResumeOutcome.NothingWaiting]);
        gateway.Called.Should().Equal("review", "publish");
    }

    [Fact]
    public async Task A_resume_that_fails_after_its_answer_was_acknowledged_is_logged()
    {
        var log = new CapturingLogger<PipelineExecutor>();
        var (waiting, gateway, executor) = Arrange(log: log);
        var runId = await WaitingRunAsync(waiting, executor);
        var nodeRunId = gateway.NodeRunIdOf(runId, waiting.Review.Id);
        gateway.RecordReadFailure = new HttpRequestException("broker unavailable");

        var taken = await executor.TakesAsAnAnswerAsync(nodeRunId, new AnswerTimings(), CancellationToken.None);

        taken.Should().BeTrue();
        await Settle.UntilAsync(() => log.Lines.Any(line => line.StartsWith($"Resuming run {runId} from node run {nodeRunId} failed", StringComparison.Ordinal)),
            "the failed resume is logged");
    }

    [Fact]
    public async Task Resuming_a_run_the_model_does_not_hold_finds_nothing_waiting()
    {
        var (_, gateway, executor) = Arrange();

        var outcome = await executor.ResumeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.NothingWaiting);
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_record_missing_on_resume_fails_the_run_naming_the_node_and_calls_nothing_again()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        var start = waiting.Fixture.Get("A submission arrives");
        gateway.Records.Remove((runId, start.Id));

        var outcome = await executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("The record of 'A submission arrives' is missing, so the run cannot resume without running it again.");
    }

    [Fact]
    public async Task A_record_a_best_effort_write_left_missing_is_written_again_with_its_watch()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var (_, gateway, executor) = Arrange(waiting);
        var refusedOnce = false;
        gateway.RefuseNodeRunWrite = (nodeId, status) =>
            nodeId == waiting.Review.Id && status == RunStatus.Waiting && !refusedOnce && (refusedOnce = true);

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        gateway.Records[(result.RunId, waiting.Review.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Watches[(result.RunId, waiting.Review.Id)].Should().Be(new Watch(Plot.Id, waiting.Decided.Id));
    }

    [Fact]
    public async Task A_waiting_record_that_cannot_be_written_fails_the_node_with_the_reason_and_leaves_no_watch()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var (_, gateway, executor) = Arrange(waiting);
        gateway.RefuseNodeRunWrite = (nodeId, status) => nodeId == waiting.Review.Id && status == RunStatus.Waiting;

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        gateway.Watches.Should().BeEmpty();
        var review = result.Nodes.Single(node => node.Name == "Review");
        review.Status.Should().Be(RunStatus.Failed);
        review.Error.Should().Be("'Review' cannot wait: the model refused the write");
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
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_Thing_neither_its_watch_nor_its_port_names()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Watches[(runId, waiting.Review.Id)] = new Watch(null, waiting.Decided.Id);
        var start = waiting.Fixture.Get("A submission arrives");
        gateway.Records[(runId, start.Id)] = gateway.Records[(runId, start.Id)] with { Outputs = new Dictionary<string, JsonElement>() };

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' waited for a Thing the model no longer names, so nothing could resume it.");
    }

    [Fact]
    public async Task A_pipeline_that_cannot_be_loaded_when_its_wait_is_answered_fails_the_run_with_the_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.LoadFailure = new PipelineModelException("Node 'Review' waits for two states, so no one answer could resume it.");

        var outcome = await executor.ResumeAsync(runId, waiting.Review.Id, CancellationToken.None);

        outcome.Should().Be(ResumeOutcome.Resumed);
        gateway.Called.Should().Equal("review");
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("Node 'Review' waits for two states, so no one answer could resume it.");
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_node_no_longer_waits()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Watches[(runId, waiting.Review.Id)] = new Watch(Plot.Id, null);
        waiting.Fixture.Unrelate(waiting.Review, waiting.ProvenBy, waiting.Decided);
        waiting.Fixture.Unrelate(waiting.Review, waiting.AwaitsThrough, waiting.Submission);
        gateway.Graph = waiting.Fixture.Build();

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' no longer waits for a state, so nothing could resume it.");
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_whose_pipeline_no_longer_loads_with_the_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Watches[(runId, waiting.Review.Id)] = new Watch(Plot.Id, null);
        gateway.LoadFailure = new PipelineModelException("Node 'Review' chose two Things for its port 'submission'.");

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("Node 'Review' chose two Things for its port 'submission'.");
    }

    [Fact]
    public async Task A_record_that_cannot_be_read_back_is_written_again_in_full_and_then_fails_the_waiting_node_retracting_its_watch()
    {
        var (waiting, gateway, executor) = Arrange();
        gateway.RecordReadFailure = new HttpRequestException("the broker is away");

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        gateway.NodeRunsPersisted.Where(node => node.Name == "Review" && node.Status == RunStatus.Waiting).Should().HaveCount(2);
        result.Nodes.Single(node => node.Name == "Review").Error
            .Should().Be("The record of 'Review' could not be written to the model, so the run could not resume from it.");
        gateway.Watches.Should().BeEmpty();
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    private static async Task<Guid> WaitingRunAsync(TestGraphs.WaitingFixture waiting, PipelineExecutor executor) =>
        (await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot)).RunId;

    [Fact]
    public async Task A_node_that_waits_writes_its_waiting_status_and_both_relationships_of_its_watch_in_one_write()
    {
        var (waiting, gateway, executor) = Arrange();

        var runId = await WaitingRunAsync(waiting, executor);

        gateway.NodeRunsPersisted.Where(node => node.Name == "Review" && node.Status == RunStatus.Waiting).Should().ContainSingle();
        gateway.WatchWrites.Should().Equal(("Review", RunStatus.Waiting, new Watch(Plot.Id, waiting.Decided.Id)));
        gateway.Watches[(runId, waiting.Review.Id)].Should().Be(new Watch(Plot.Id, waiting.Decided.Id));
    }

    [Fact]
    public async Task An_answer_naming_the_node_run_resumes_the_run_and_ends_the_wait_in_one_write()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);

        var taken = await executor.TakesAsAnAnswerAsync(gateway.NodeRunIdOf(runId, waiting.Review.Id), new AnswerTimings(), CancellationToken.None);

        taken.Should().BeTrue();
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) == RunStatus.Succeeded, "the answer resumes the run");
        gateway.Called.Should().Equal("review", "publish");
        gateway.ResumesWritten.Should().Equal((runId, waiting.Review.Id));
        gateway.WatchWrites.Last().Should().Be(("Review", RunStatus.Succeeded, new Watch(Plot.Id, waiting.Decided.Id)));
        gateway.NodeRunsPersisted.Where(node => node.Name == "Review" && node.Status == RunStatus.Succeeded).Should().ContainSingle();
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Thing_that_is_not_a_node_run_is_not_taken_for_an_answer()
    {
        var (_, _, executor) = Arrange();

        (await executor.TakesAsAnAnswerAsync(Plot.Id, new AnswerTimings(), CancellationToken.None)).Should().BeFalse();
    }

    // The platform answers a Thing already in the range as the watch commits, while the sweep that wrote it still holds the run.
    [Fact]
    public async Task An_answer_arriving_while_the_placing_sweep_holds_the_run_resumes_it_once_that_sweep_ends()
    {
        var (waiting, gateway, executor) = Arrange();
        Task<bool>? answering = null;
        gateway.OnWatchPlaced = nodeRunId => answering ??= executor.TakesAsAnAnswerAsync(nodeRunId, new AnswerTimings(), CancellationToken.None);

        var paused = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        paused.Error.Should().Be("The run waits for 'Plot 7' to enter 'decided' at 'Review'.");
        answering.Should().NotBeNull();
        (await answering!).Should().BeTrue();
        await Settle.UntilAsync(() => gateway.RunStatusOf(paused.RunId) == RunStatus.Succeeded, "the early answer resumes the run");
        gateway.StatusUpdates.Should().ContainInOrder(RunStatus.Waiting, RunStatus.Running, RunStatus.Succeeded);
        gateway.Called.Should().Equal("review", "publish");
        gateway.ResumesWritten.Should().ContainSingle();
    }

    [Fact]
    public async Task A_second_answer_to_the_same_wait_resumes_nothing()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        var nodeRunId = gateway.NodeRunIdOf(runId, waiting.Review.Id);

        await executor.TakesAsAnAnswerAsync(nodeRunId, new AnswerTimings(), CancellationToken.None);
        await executor.TakesAsAnAnswerAsync(nodeRunId, new AnswerTimings(), CancellationToken.None);

        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) == RunStatus.Succeeded, "the first answer resumes the run");
        await executor.TakesAsAnAnswerAsync(nodeRunId, new AnswerTimings(), CancellationToken.None);
        gateway.Called.Should().Equal("review", "publish");
        gateway.ResumesWritten.Should().ContainSingle();
    }

    [Fact]
    public async Task A_sweep_another_branch_ends_early_retracts_the_watch_of_its_waiting_node_in_that_nodes_own_write()
    {
        var (waiting, gateway, executor) = Arrange(TestGraphs.WaitingPipeline(approvedBeside: true));
        var approve = waiting.Fixture.Get("Approve");
        gateway.OnCall = (subdomain, _) => subdomain switch
        {
            "review" => NodeOk(("notice", "sent for review")),
            _ => NodeFail($"{subdomain} is away"),
        };

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        var ended = gateway.WatchWrites.Where(write => write.Name == "Review" && write.Status != RunStatus.Waiting).Should().ContainSingle().Subject;
        ended.Watch.Should().Be(new Watch(Plot.Id, waiting.Decided.Id));
        gateway.Records[(result.RunId, waiting.Review.Id)].Status.Should().Be(ended.Status);
        gateway.Watches.Should().BeEmpty();
        gateway.Records[(result.RunId, approve.Id)].Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_watch_the_platform_refuses_fails_the_node_with_its_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        gateway.WatchRefusal = "Range 'decided' does not judge 'Plot 7'";

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);

        var review = result.Nodes.Single(node => node.Name == "Review");
        review.Status.Should().Be(RunStatus.Failed);
        review.Error.Should().Be("'Review' cannot wait: Range 'decided' does not judge 'Plot 7'");
        gateway.Watches.Should().BeEmpty();
        gateway.StatusUpdates.Last().Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_restarted_orchestrator_resumes_a_waiting_node_run_whose_answer_the_platform_already_holds_as_done()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Answers[(runId, waiting.Review.Id)] = WaitAnswer.Done;

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Called.Should().Equal("review", "publish");
        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Succeeded);
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_restarted_orchestrator_leaves_a_wait_alone_while_the_platform_is_still_answering_it()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Answers[(runId, waiting.Review.Id)] = WaitAnswer.Underway;
        var writesBefore = gateway.NodeRunsPersisted.Count;

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Called.Should().Equal("review");
        gateway.NodeRunsPersisted.Should().HaveCount(writesBefore);
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Waiting);
    }

    [Fact]
    public async Task A_restarted_orchestrator_leaves_a_whole_unanswered_watch_alone()
    {
        var (waiting, gateway, executor) = Arrange();
        await WaitingRunAsync(waiting, executor);
        var writesBefore = gateway.NodeRunsPersisted.Count;

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.NodeRunsPersisted.Should().HaveCount(writesBefore);
    }

    // A model copied from elsewhere can arrive without a relationship to a Thing the copy did not carry.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task A_restarted_orchestrator_writes_a_watch_missing_a_relationship_again_reading_the_awaited_Thing_from_its_port(bool keepsAwaits, bool keepsWatches)
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Watches[(runId, waiting.Review.Id)] = new Watch(keepsAwaits ? Plot.Id : null, keepsWatches ? waiting.Decided.Id : null);

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Watches[(runId, waiting.Review.Id)].Should().Be(new Watch(Plot.Id, waiting.Decided.Id));
        gateway.WatchWrites.Last().Should().Be(("Review", RunStatus.Waiting, new Watch(Plot.Id, waiting.Decided.Id)));
        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Called.Should().Equal("review");
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_wait_it_cannot_watch_again_with_the_platforms_reason()
    {
        var (waiting, gateway, executor) = Arrange();
        var runId = await WaitingRunAsync(waiting, executor);
        gateway.Watches[(runId, waiting.Review.Id)] = new Watch(null, waiting.Decided.Id);
        gateway.WatchRefusal = "No Thing 'Plot 7' in the model";

        await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        gateway.Records[(runId, waiting.Review.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Review' cannot wait: No Thing 'Plot 7' in the model");
        gateway.Watches.Should().BeEmpty();
    }

    private static NodeCallResult NodeOk(params (string Port, string Value)[] outputs) =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = outputs.ToDictionary(o => o.Port, o => o.Value) }));

    private static NodeCallResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { result = "fail", error }));
}
