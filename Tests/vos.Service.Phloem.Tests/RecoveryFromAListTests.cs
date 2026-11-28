using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class RecoveryFromAListTests
{
    private static readonly TimeSpan AWhile = TimeSpan.FromMilliseconds(300);

    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static NodeCallResult Succeeding => new(200, """{"result":"success","outputs":{"echo":"x","out":"x","notice":"sent"}}""");

    private sealed class HeldCall
    {
        public TaskCompletionSource Called { get; } = New();
        public TaskCompletionSource Answered { get; } = New();
    }

    private static HeldCall Holding(FakeGateway gateway, string subdomain)
    {
        var call = new HeldCall();
        gateway.OnCallAsync = async (called, _) =>
        {
            if (called == subdomain)
            {
                call.Called.TrySetResult();
                await call.Answered.Task;
            }
            return Succeeding;
        };
        return call;
    }

    private sealed class OrchestratorStartingAgain
    {
        private readonly CapturingLogger<MyceliumGateway> _gatewayLog = new();
        private readonly CapturingLogger<StartUpSequence> _sequenceLog = new();

        public OrchestratorStartingAgain(FakeGateway gateway)
        {
            Executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock(), beginnings: Beginnings);
            var broker = new MyceliumGateway(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
                _gatewayLog, "http://localhost:7243", new ModelClock(), new LaunchToken("svc-token"));
            Sequence = new StartUpSequence(broker, Executor, Beginnings, _sequenceLog, port: 7300);
        }

        public BeginningGate Beginnings { get; } = new();
        public PipelineExecutor Executor { get; }
        public StartUpSequence Sequence { get; }
        public IEnumerable<string> ErrorLines => _gatewayLog.ErrorLines.Concat(_sequenceLog.ErrorLines);
    }

    private static PipelineExecutor Earlier(FakeGateway gateway) => new(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

    private static (FakeGateway Gateway, Guid PipelineId) Demo()
    {
        var (drawing, pipelineId) = TestGraphs.DemoPipeline();
        return (new FakeGateway(drawing.Build()) { FreshGraph = () => drawing.Build() }, pipelineId);
    }

    private static (FakeGateway Gateway, TestGraphs.WaitingFixture Waiting, RunSubject Subject) Waiting()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var gateway = new FakeGateway(waiting.Fixture.Build()) { FreshGraph = () => waiting.Fixture.Build() };
        return (gateway, waiting, new RunSubject(waiting.Fixture.Thing("Plot 7").Id, "Plot 7"));
    }

    private static async Task<Guid> WaitingRunAsync(PipelineExecutor executor, FakeGateway gateway, TestGraphs.WaitingFixture waiting, RunSubject subject)
    {
        var run = (await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: subject)).RunId;
        await Settle.UntilAsync(() => gateway.RunStatusOf(run) == RunStatus.Waiting, "the run waits");
        return run;
    }

    [Fact]
    public async Task A_run_begun_while_the_recovery_is_listing_begins_after_the_list_and_is_never_carried_on_by_it()
    {
        var (gateway, pipelineId) = Demo();
        var generate = Holding(gateway, "gen");
        var listingLetGo = New();
        gateway.ListingHeldUntil = listingLetGo.Task;
        var restart = new OrchestratorStartingAgain(gateway);
        var starting = restart.Sequence.RunAsync(CancellationToken.None);
        await Settle.ForAsync(gateway.Listed, "the recovery reads what the orchestrator holds");

        var run = Guid.NewGuid();
        var running = restart.Executor.RunAsync(pipelineId, default, CancellationToken.None, run);
        await Settle.BeforeAssertingAbsenceAsync(AWhile);
        gateway.RunStatusOf(run).Should().BeNull("no run begins while the recovery is listing");

        listingLetGo.SetResult();
        await Settle.ForAsync(generate.Called.Task, "the run begins once the list is assembled");
        await Settle.ForAsync(starting, "the recovery ends while the run is still in its call, because the run is not in its list");
        generate.Answered.SetResult();
        await Settle.ForAsync(running, "the run ends");

        (await running).Success.Should().BeTrue();
        gateway.Called.Count(name => name == "gen").Should().Be(1);
    }

    [Fact]
    public async Task A_run_begun_after_the_list_and_held_in_a_long_call_holds_up_no_listed_runs_recovery()
    {
        var (gateway, waiting, subject) = Waiting();
        var review = Holding(gateway, "review");
        var left = Guid.NewGuid();
        gateway.Runs[left] = (waiting.PipelineId, JsonSerializer.SerializeToElement(new { subject = new { id = subject.Id, name = subject.Name } }));
        gateway.RunStatuses[left] = RunStatus.Running;
        gateway.Records[(left, waiting.Fixture.Get("A submission arrives").Id)] = new NodeRecord(RunStatus.Succeeded,
            new Dictionary<string, JsonElement> { ["subject"] = JsonSerializer.SerializeToElement(new { id = subject.Id, name = subject.Name }) });
        gateway.Records[(left, waiting.Review.Id)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());
        var listingLetGo = New();
        gateway.ListingHeldUntil = listingLetGo.Task;
        var restart = new OrchestratorStartingAgain(gateway);
        var starting = restart.Sequence.RunAsync(CancellationToken.None);
        await Settle.ForAsync(gateway.Listed, "the recovery reads what the orchestrator holds");

        var live = Guid.NewGuid();
        _ = restart.Executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, live, subject);
        listingLetGo.SetResult();
        await Settle.ForAsync(review.Called.Task, "the run begun after the list calls its node");

        await Settle.ForAsync(starting, "the recovery ends while that run is still in its call");
        gateway.RunStatusOf(left).Should().Be(RunStatus.Failed, "the run an earlier orchestrator left in a call is ended, since its service may already have been called");
        gateway.RunStatusOf(live).Should().Be(RunStatus.Running, "the run begun after the list is still in its call");
        review.Answered.SetResult();
        await Settle.UntilAsync(() => gateway.RunStatusOf(live) == RunStatus.Waiting, "the run begun after the list goes on to its wait");
    }

    [Fact]
    public async Task A_stop_and_a_waits_answer_are_taken_while_the_recovery_is_listing()
    {
        var (gateway, waiting, subject) = Waiting();
        gateway.OnCallAsync = (_, _) => Task.FromResult(Succeeding);
        var answered = await WaitingRunAsync(Earlier(gateway), gateway, waiting, subject);
        var stopped = await WaitingRunAsync(Earlier(gateway), gateway, waiting, subject);
        var listingLetGo = New();
        gateway.ListingHeldUntil = listingLetGo.Task;
        var restart = new OrchestratorStartingAgain(gateway);
        var starting = restart.Sequence.RunAsync(CancellationToken.None);
        await Settle.ForAsync(gateway.Listed, "the recovery reads what the orchestrator holds");

        (await restart.Executor.TakesAsAnAnswerAsync(gateway.NodeRunIdOf(answered, waiting.Review.Id), new AnswerTimings(), CancellationToken.None))
            .Should().BeTrue("the answer is taken at once");
        await Settle.ForAsync(restart.Executor.StopAsync(Guid.NewGuid(), stopped, CancellationToken.None), "the stop is carried out while the recovery is listing");
        RunStatus.IsFinished(gateway.RunStatusOf(stopped)!).Should().BeTrue();
        await Settle.BeforeAssertingAbsenceAsync(AWhile);
        gateway.RunStatusOf(answered).Should().Be(RunStatus.Waiting, "resuming the run begins its sweep again, so it waits for the list");

        listingLetGo.SetResult();
        await Settle.ForAsync(starting, "the recovery ends");
        await Settle.UntilAsync(() => gateway.RunStatusOf(answered) == RunStatus.Succeeded, "the answer resumes the run once the list is assembled");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task When_the_listing_is_refused_or_fails_runs_begin_and_the_failure_is_logged(HttpStatusCode answer)
    {
        var (gateway, pipelineId) = Demo();
        gateway.ListingHeldUntil = Task.FromException(new HttpRequestException($"{(int)answer} ({answer})", null, answer));
        var restart = new OrchestratorStartingAgain(gateway);

        await Settle.ForAsync(restart.Sequence.RunAsync(CancellationToken.None), "the start-up sequence ends");
        var running = restart.Executor.RunAsync(pipelineId, default, CancellationToken.None);
        await Settle.ForAsync(running, "a run begins although nothing was listed");

        (await running).Success.Should().BeTrue();
        restart.ErrorLines.Should().ContainSingle().Which.Should().Contain(((int)answer).ToString());
    }

    [Fact]
    public async Task A_program_call_waiting_for_the_list_gives_up_when_its_caller_goes_away()
    {
        var (gateway, pipelineId) = Demo();
        var restart = new OrchestratorStartingAgain(gateway);
        using var caller = new CancellationTokenSource();
        var run = Guid.NewGuid();

        var waitingForTheList = restart.Executor.RunAsync(pipelineId, default, caller.Token, run, waitsForItsPlace: true);
        await Settle.BeforeAssertingAbsenceAsync(AWhile);
        waitingForTheList.IsCompleted.Should().BeFalse("nothing has listed what the orchestrator holds");
        caller.Cancel();

        await FluentActions.Awaiting(() => waitingForTheList).Should().ThrowAsync<OperationCanceledException>();
        gateway.RunStatusOf(run).Should().BeNull();
    }

    [Fact]
    public async Task A_queued_run_that_was_listed_begins_once()
    {
        var (gateway, pipelineId) = Demo();
        var queued = Guid.NewGuid();
        await gateway.CreateRunAsync(queued, pipelineId, CancellationToken.None, queueNumber: 1);
        var restart = new OrchestratorStartingAgain(gateway);

        await Settle.ForAsync(restart.Sequence.RunAsync(CancellationToken.None), "the start-up sequence ends");
        await Settle.UntilAsync(() => gateway.RunStatusOf(queued) == RunStatus.Succeeded, "the queued run begins and ends");
        await Settle.BeforeAssertingAbsenceAsync(AWhile);

        gateway.BeginsAsked.Should().Be(1);
        gateway.Called.Count(name => name == "gen").Should().Be(1);
    }

    [Fact]
    public async Task The_four_recoveries_are_served_by_one_read_of_the_held_runs()
    {
        var (gateway, _) = Demo();
        var restart = new OrchestratorStartingAgain(gateway);

        await Settle.ForAsync(restart.Sequence.RunAsync(CancellationToken.None), "the start-up sequence ends");

        gateway.ListingsRead.Should().Be(1);
    }

    [Fact]
    public async Task A_beginning_waiting_for_the_list_when_the_service_stops_is_cancelled_and_records_no_run()
    {
        var (gateway, pipelineId) = Demo();
        var listingLetGo = New();
        gateway.ListingHeldUntil = listingLetGo.Task;
        var restart = new OrchestratorStartingAgain(gateway);
        using var stopping = new CancellationTokenSource();
        var starting = restart.Sequence.RunAsync(stopping.Token);
        await Settle.ForAsync(gateway.Listed, "the recovery reads what the orchestrator holds");
        var run = Guid.NewGuid();
        var beginning = restart.Executor.RunAsync(pipelineId, default, CancellationToken.None, run);

        stopping.Cancel();
        await Settle.ForAsync(beginning, "the beginning gives up once the orchestrator is stopping");
        listingLetGo.SetResult();
        await Settle.ForAsync(starting, "the start-up sequence ends");

        var result = await beginning;
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("stopping");
        gateway.RunStatusOf(run).Should().BeNull("a beginning cancelled at the gate records nothing");
    }

    [Fact]
    public async Task A_listed_waiting_run_whose_answer_also_arrives_live_is_resumed_once()
    {
        var (gateway, waiting, subject) = Waiting();
        gateway.OnCallAsync = (_, _) => Task.FromResult(Succeeding);
        var run = await WaitingRunAsync(Earlier(gateway), gateway, waiting, subject);
        gateway.Answers[(run, waiting.Review.Id)] = WaitAnswer.Done;
        var listingLetGo = New();
        gateway.ListingHeldUntil = listingLetGo.Task;
        var restart = new OrchestratorStartingAgain(gateway);
        var starting = restart.Sequence.RunAsync(CancellationToken.None);
        await Settle.ForAsync(gateway.Listed, "the recovery reads what the orchestrator holds");

        var live = restart.Executor.ResumeAsync(run, waiting.Review.Id, CancellationToken.None);
        listingLetGo.SetResult();
        await Settle.ForAsync(starting, "the recovery ends");
        await Settle.ForAsync(live, "the live answer is taken");
        await Settle.UntilAsync(() => gateway.RunStatusOf(run) == RunStatus.Succeeded, "the run ends");
        await Settle.BeforeAssertingAbsenceAsync(AWhile);

        gateway.ResumesWritten.Should().ContainSingle();
        gateway.Called.Count(name => name == "publish").Should().Be(1);
    }
}
