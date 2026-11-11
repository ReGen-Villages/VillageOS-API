using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A wait box is run by the orchestrator: it calls no service, holds the run until the Thing reaching it enters a state,
// and hands on that Thing and what it reaches along the predicate the box follows.
public class WaitBoxTests
{
    private static readonly RunSubject Submission = new(Guid.NewGuid(), "Submission 12");
    private static readonly RunSubject Promoted = new(Guid.NewGuid(), "promoted");

    private static (TestGraphs.WaitBoxFixture Box, FakeGateway Gateway, PipelineExecutor Executor) Arrange(TestGraphs.WaitBoxFixture? fixture = null)
    {
        var box = fixture ?? TestGraphs.WaitBoxPipeline();
        var gateway = new FakeGateway(box.Fixture.Build())
        {
            OnCall = (_, envelope) => new NodeCallResult(200, JsonSerializer.Serialize(
                new { result = "success", outputs = new { @out = envelope.GetProperty("inputs").GetProperty("in") } })),
        };
        return (box, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    private static JsonElement PublishedIn(FakeGateway gateway) =>
        gateway.Envelopes.Single(sent => sent.Subdomain == "publish").Envelope.GetProperty("inputs").GetProperty("in");

    [Fact]
    public async Task A_run_reaching_a_wait_box_calls_no_service_and_waits_for_the_Thing_to_enter_the_state()
    {
        var (box, gateway, executor) = Arrange();

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        result.Error.Should().Be($"The run waits for 'Submission 12' to enter 'decided' at '{TestGraphs.WaitBox}'.");
        gateway.Called.Should().BeEmpty();
        gateway.Records[(result.RunId, box.Wait.Id)].Status.Should().Be(RunStatus.Waiting);
        gateway.Watches[(result.RunId, box.Wait.Id)].Should().Be(new Watch(Submission.Id, box.Decided.Id));
    }

    [Fact]
    public async Task A_resumed_wait_box_hands_on_the_Thing_it_waited_for()
    {
        var (box, gateway, executor) = Arrange();
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        await executor.ResumeAsync(waited.RunId, box.Wait.Id, CancellationToken.None);

        PublishedIn(gateway).GetProperty("id").GetGuid().Should().Be(Submission.Id);
        gateway.Records[(waited.RunId, box.Wait.Id)].Outputs.Should().NotContainKey(ModelNames.Reached);
    }

    [Fact]
    public async Task A_resumed_wait_box_that_follows_a_predicate_hands_on_the_Things_reached_along_it()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(follows: true));
        gateway.ReachedAlong[(Submission.Id, box.ResolvedAs!.Id)] = [Promoted];
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        await executor.ResumeAsync(waited.RunId, box.Wait.Id, CancellationToken.None);

        var reached = PublishedIn(gateway).EnumerateArray().Single();
        reached.GetProperty("id").GetGuid().Should().Be(Promoted.Id);
        reached.GetProperty("name").GetString().Should().Be("promoted");
    }

    [Fact]
    public async Task A_wait_box_whose_Thing_reaches_nothing_along_the_predicate_hands_on_an_empty_list()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(follows: true));
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        await executor.ResumeAsync(waited.RunId, box.Wait.Id, CancellationToken.None);

        PublishedIn(gateway).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_wait_box_that_cannot_read_what_its_Thing_reaches_fails_and_calls_nothing_after_it()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(follows: true));
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);
        gateway.ReachedAlongFailure = new HttpRequestException("503 from the broker");

        await executor.ResumeAsync(waited.RunId, box.Wait.Id, CancellationToken.None);

        gateway.Called.Should().BeEmpty();
        gateway.Records[(waited.RunId, box.Wait.Id)].Status.Should().Be(RunStatus.Failed);
        gateway.Records[(waited.RunId, box.Wait.Id)].Error.Should().Be(
            $"'{TestGraphs.WaitBox}' could not read what 'Submission 12' reaches along 'resolvedAs': 503 from the broker");
        gateway.Watches.Should().BeEmpty();
        gateway.RunStatusOf(waited.RunId).Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_restarted_orchestrator_resumes_a_wait_box_the_platform_answered_while_it_was_away()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(follows: true));
        gateway.ReachedAlong[(Submission.Id, box.ResolvedAs!.Id)] = [Promoted];
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);
        gateway.Answers[(waited.RunId, box.Wait.Id)] = WaitAnswer.Done;

        var recovered = await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        recovered.Resumed.Should().Be(1);
        PublishedIn(gateway).EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(Promoted.Id);
    }

    [Fact]
    public async Task A_restarted_orchestrator_watches_again_for_a_wait_box_whose_watch_was_lost()
    {
        var (box, gateway, executor) = Arrange();
        var waited = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);
        gateway.Watches[(waited.RunId, box.Wait.Id)] = new Watch(null, box.Decided.Id);

        var recovered = await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);

        recovered.WatchedAgain.Should().Be(1);
        gateway.Watches[(waited.RunId, box.Wait.Id)].Should().Be(new Watch(Submission.Id, box.Decided.Id));
    }

    [Fact]
    public async Task A_wait_box_that_follows_two_predicates_refuses_the_run()
    {
        var box = TestGraphs.WaitBoxPipeline(follows: true);
        box.Fixture.Rel(box.Wait, box.Fixture.Get("reachedAlong"), box.Fixture.Thing("proposes"));
        var (_, gateway, executor) = Arrange(box);

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        result.Error.Should().Contain($"Node '{TestGraphs.WaitBox}' follows 2 predicates; a wait node hands on what one reaches.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_rehearsal_records_what_a_wait_box_would_have_waited_for_and_places_no_watch()
    {
        var (box, gateway, executor) = Arrange();

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission, rehearsal: true);

        gateway.Records[(result.RunId, box.Wait.Id)].Status.Should().Be(RunStatus.Rehearsed);
        gateway.Records[(result.RunId, box.Wait.Id)].Outputs[ModelNames.Thing].GetProperty("id").GetGuid().Should().Be(Submission.Id);
        gateway.Watches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wait_box_that_names_no_state_refuses_the_run_before_anything_runs()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(namesAState: false));

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        result.Error.Should().Contain($"Node '{TestGraphs.WaitBox}' waits through its port 'thing' but names no state for it to enter.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_wait_box_with_nothing_wired_into_its_port_refuses_the_run_before_anything_runs()
    {
        var (box, gateway, executor) = Arrange(TestGraphs.WaitBoxPipeline(wired: false));

        var result = await executor.RunAsync(box.PipelineId, default, CancellationToken.None, subject: Submission);

        result.Error.Should().Contain($"'{TestGraphs.WaitBox}' waits for the Thing reaching its port 'thing', but nothing fills that port.");
        gateway.RunStatusOf(result.RunId).Should().Be(RunStatus.Refused);
    }

    [Fact]
    public async Task A_wait_box_reached_by_something_that_names_no_Thing_fails_naming_the_port()
    {
        var (box, gateway, executor) = Arrange();

        var result = await executor.RunAsync(box.PipelineId, JsonSerializer.SerializeToElement(new { subject = "not a Thing" }), CancellationToken.None);

        result.Nodes.Single(node => node.NodeId == box.Wait.Id).Error.Should().Be(
            $"'{TestGraphs.WaitBox}' waits for the Thing reaching its port 'thing', but what reached it names no Thing.");
        gateway.Watches.Should().BeEmpty();
    }
}
