using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Service.Shared.ServiceCalls;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// Generate, the first node of every run here, answers when the test says so.
public class RunsAtOnceTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    private readonly GraphFixture _drawing;
    private readonly Guid _pipelineId;
    private readonly FakeGateway _gateway;
    private readonly PipelineExecutor _executor;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<NodeCallResult>> _answers = new();
    private readonly List<TimeSpan> _pauses = new();
    private readonly ApplicationLifetime _lifetime = new(NullLogger<ApplicationLifetime>.Instance);
    private readonly CapturingLogger<PipelineExecutor> _log = new();
    private Func<Task> _duringAPause;

    public RunsAtOnceTests()
    {
        (_drawing, _pipelineId) = TestGraphs.DemoPipeline();
        _gateway = new FakeGateway(_drawing.Build()) { FreshGraph = () => _drawing.Build(), OnCallAsync = CallAsync };
        _duringAPause = () =>
        {
            _gateway.RecordReadFailure = null;
            _gateway.LoadFailure = null;
            return Task.CompletedTask;
        };
        _executor = new PipelineExecutor(_gateway, _log, new ModelClock(), pause: length =>
        {
            lock (_pauses) _pauses.Add(length);
            return _duringAPause();
        }, lifetime: _lifetime);
    }

    private int Pauses
    {
        get { lock (_pauses) return _pauses.Count; }
    }

    private async Task<TaskCompletionSource> TheModelIsNeverReadAgainAfterAsync(Guid runId, Action<Exception> failing)
    {
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _duringAPause = () => pause.Task;
        _gateway.OnRunFinished = () => failing(new HttpRequestException("the broker is away"));
        AnswerOf(runId).SetResult(Succeeding("echo"));
        await Settle.UntilAsync(() => Pauses == 1, "the next run's turn came and the model could not be read");
        _gateway.OnRunFinished = () => { };
        return pause;
    }

    private Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope) =>
        subdomain == "gen" ? AnswerOf(envelope.GetProperty("runId").GetGuid()).Task : Task.FromResult(Succeeding("echo"));

    private TaskCompletionSource<NodeCallResult> AnswerOf(Guid runId) =>
        _answers.GetOrAdd(runId, _ => new TaskCompletionSource<NodeCallResult>(TaskCreationOptions.RunContinuationsAsynchronously));

    private static NodeCallResult Succeeding(string port) =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = new Dictionary<string, string> { [port] = "x" } }));

    private static NodeCallResult Failing => new(200, JsonSerializer.Serialize(new { result = "fail", error = "the source is away" }));

    private void Limit(object runsAtOnce) => _drawing.State(_drawing.Get("Demo"), ModelNames.RunsAtOnce, runsAtOnce);

    private async Task<Guid> StartAsync(RunSubject? subject = null, Guid? callRecordId = null, bool rehearsal = false)
    {
        var runId = Guid.NewGuid();
        _ = Task.Run(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, runId, subject, rehearsal: rehearsal, callRecordId: callRecordId));
        await Settle.UntilAsync(() => _gateway.RunStatusOf(runId) is not null, "the run is recorded");
        if (_gateway.RunStatusOf(runId) == RunStatus.Running) await BegunAsync(runId);
        return runId;
    }

    private async Task<List<Guid>> StartAsync(int howMany)
    {
        var runs = new List<Guid>();
        for (var run = 0; run < howMany; run++) runs.Add(await StartAsync());
        return runs;
    }

    private List<Guid> Begun()
    {
        lock (_gateway.Called)
            return _gateway.Envelopes.Where(call => call.Subdomain == "gen").Select(call => call.Envelope.GetProperty("runId").GetGuid()).ToList();
    }

    private Task BegunAsync(Guid runId) => Settle.UntilAsync(() => Begun().Contains(runId), "the run begins");

    private async Task FinishAsync(Guid runId)
    {
        AnswerOf(runId).TrySetResult(Succeeding("echo"));
        await StatusAsync(runId, RunStatus.Succeeded);
    }

    private Task StatusAsync(Guid runId, string status) =>
        Settle.UntilAsync(() => _gateway.RunStatusOf(runId) == status, $"the run is {status}");

    private async Task NothingElseBeginsAsync(params Guid[] begun)
    {
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        Begun().Should().Equal(begun);
    }

    private static async Task<T> InTimeAsync<T>(Task<T> work, string expectation)
    {
        await Settle.ForAsync(work, expectation);
        return await work;
    }

    private Task StoppedInTimeAsync(Guid targetId) => Settle.ForAsync(
        _executor.StopAsync(Guid.NewGuid(), targetId, CancellationToken.None), "the stop ends the run it names");

    private Task NoPlaceIsHeldAsync() => Settle.UntilAsync(() => _executor.PipelinesHoldingPlaces == 0, "no place is held and nothing is queued");

    [Fact]
    public async Task With_a_limit_of_one_each_later_start_is_queued_and_begins_when_the_run_before_it_finishes()
    {
        Limit(1);

        var runs = await StartAsync(3);

        runs.Select(_gateway.RunStatusOf).Should().Equal(RunStatus.Running, RunStatus.Queued, RunStatus.Queued);
        await NothingElseBeginsAsync(runs[0]);
        await FinishAsync(runs[0]);
        await BegunAsync(runs[1]);
        await NothingElseBeginsAsync(runs[0], runs[1]);
        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Running);
        await FinishAsync(runs[1]);
        await BegunAsync(runs[2]);
        await FinishAsync(runs[2]);
        await NoPlaceIsHeldAsync();
    }

    [Fact]
    public async Task With_no_limit_every_run_begins_at_once_with_no_queue_number_and_no_place_held()
    {
        var runs = await StartAsync(8);

        Begun().Should().BeEquivalentTo(runs);
        _gateway.QueueNumbers.Should().BeEmpty();
        _executor.PipelinesHoldingPlaces.Should().Be(0);
    }

    [Fact]
    public async Task A_queued_run_begins_with_the_parameters_and_the_rehearsal_it_was_started_with()
    {
        Limit(1);
        var first = await StartAsync();
        var second = await StartAsync(Plot, rehearsal: true);
        _gateway.RunStatusOf(second).Should().Be(RunStatus.Queued);

        await FinishAsync(first);
        await StatusAsync(second, RunStatus.CannotRehearse);

        _gateway.Runs[second].Parameters.GetProperty("subject").GetProperty("id").GetGuid().Should().Be(Plot.Id);
        Begun().Should().Equal(first);
    }

    [Fact]
    public async Task A_run_that_fails_leaves_its_place()
    {
        Limit(1);
        var runs = await StartAsync(2);

        AnswerOf(runs[0]).SetResult(Failing);

        await BegunAsync(runs[1]);
        _gateway.RunStatusOf(runs[0]).Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task A_run_stopped_while_it_goes_leaves_its_place()
    {
        Limit(1);
        var runs = await StartAsync(2);
        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Queued);

        var stopping = StoppedInTimeAsync(runs[0]);
        AnswerOf(runs[0]).SetResult(Succeeding("echo"));
        await stopping;

        await BegunAsync(runs[1]);
        _gateway.RunStatusOf(runs[0]).Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task A_run_whose_sweep_throws_leaves_its_place()
    {
        Limit(1);
        using var caller = new CancellationTokenSource();
        var first = Guid.NewGuid();
        var running = Task.Run(() => _executor.RunAsync(_pipelineId, default, caller.Token, first));
        await BegunAsync(first);
        var second = await StartAsync();
        _gateway.RunStatusOf(second).Should().Be(RunStatus.Queued);

        await caller.CancelAsync();
        AnswerOf(first).SetResult(Succeeding("echo"));

        await FluentActions.Awaiting(() => Settle.ForAsync(running, "the run whose caller went away ends")).Should().ThrowAsync<OperationCanceledException>();
        await BegunAsync(second);
    }

    [Fact]
    public async Task A_refused_start_takes_no_place()
    {
        Limit(1);
        _gateway.LoadFailure = new HttpRequestException("the model could not be read");
        var refused = await StartAsync();
        _gateway.LoadFailure = null;

        var next = await StartAsync();

        _gateway.RunStatusOf(refused).Should().Be(RunStatus.Refused);
        _gateway.RunStatusOf(next).Should().Be(RunStatus.Running);
        _gateway.RunStatusOf(await StartAsync()).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task A_run_that_could_not_be_recorded_leaves_the_place_it_took()
    {
        Limit(1);
        _gateway.RunCreationFailure = new HttpRequestException("the model refused the run");
        await _executor.RunAsync(_pipelineId, default, CancellationToken.None);
        _gateway.RunCreationFailure = null;

        var next = await StartAsync();

        _gateway.RunStatusOf(next).Should().Be(RunStatus.Running);
        _gateway.RunStatusOf(await StartAsync()).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task A_caller_that_goes_away_while_its_run_is_recorded_leaves_the_place_it_took()
    {
        Limit(1);
        using var caller = new CancellationTokenSource();
        var recording = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateway.RunCreationHeldUntil = recording.Task;
        var starting = Task.Run(() => _executor.RunAsync(_pipelineId, default, caller.Token));
        await Settle.UntilAsync(() => _gateway.RunCreationStarted, "the run is being recorded");
        await caller.CancelAsync();
        recording.SetException(new OperationCanceledException());

        await FluentActions.Awaiting(() => Settle.ForAsync(starting, "the start ends")).Should().ThrowAsync<OperationCanceledException>();

        _gateway.RunCreationHeldUntil = Task.CompletedTask;
        _gateway.RunStatusOf(await StartAsync()).Should().Be(RunStatus.Running);
        _gateway.RunStatusOf(await StartAsync()).Should().Be(RunStatus.Queued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_queued_run_that_could_not_be_recorded_holds_up_none_queued_after_it(bool waitsForItsPlace)
    {
        Limit(1);
        var first = await StartAsync();
        _gateway.RunCreationFailure = new HttpRequestException("the model refused the run");
        var unrecorded = await InTimeAsync(_executor.RunAsync(_pipelineId, default, CancellationToken.None, waitsForItsPlace: waitsForItsPlace),
            "a start that could not be recorded is answered at once");
        _gateway.RunCreationFailure = null;
        var third = await StartAsync();
        _gateway.RunStatusOf(third).Should().Be(RunStatus.Queued);

        await FinishAsync(first);

        unrecorded.Error.Should().Be("the model refused the run");
        await BegunAsync(third);
        await FinishAsync(third);
        await NoPlaceIsHeldAsync();
    }

    [Fact]
    public async Task A_run_waiting_for_a_state_gives_its_place_up_and_goes_on_past_the_limit_when_it_resumes()
    {
        var waiting = TestGraphs.WaitingPipeline();
        waiting.Fixture.State(waiting.Fixture.Get("Review a submission"), ModelNames.RunsAtOnce, 1);
        var secondReview = new TaskCompletionSource<NodeCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reviews = 0;
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            FreshGraph = () => waiting.Fixture.Build(),
            OnCallAsync = (subdomain, _) => subdomain == "publish" ? Task.FromResult(Succeeding("out"))
                : Interlocked.Increment(ref reviews) == 1 ? Task.FromResult(Succeeding("notice")) : secondReview.Task,
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var paused = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        var going = Task.Run(() => executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot));
        await Settle.UntilAsync(() => gateway.CalledSoFar().Count == 2, "the second run takes the place the waiting run gave up");
        var queued = await InTimeAsync(executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot), "a queued start is answered at once");

        await InTimeAsync(executor.ResumeAsync(paused.RunId, waiting.Review.Id, CancellationToken.None), "the run resumes although the pipeline is full");

        gateway.RunStatusOf(queued.RunId).Should().Be(RunStatus.Queued);
        gateway.RunStatusOf(paused.RunId).Should().Be(RunStatus.Succeeded);
        gateway.CalledSoFar().Should().Equal("review", "review", "publish");
        secondReview.SetResult(Succeeding("notice"));
        await InTimeAsync(going, "the second run waits in its turn");
        await Settle.UntilAsync(() => gateway.RunStatusOf(queued.RunId) == RunStatus.Waiting, "the queued run begins once the second waits in its turn");
    }

    [Fact]
    public async Task A_queued_run_that_is_stopped_is_recorded_cancelled_at_once_and_its_turn_passes_to_the_next()
    {
        Limit(1);
        var runs = await StartAsync(3);

        await StoppedInTimeAsync(runs[1]);

        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Cancelled);
        await FinishAsync(runs[0]);
        await BegunAsync(runs[2]);
        await NothingElseBeginsAsync(runs[0], runs[2]);
        await FinishAsync(runs[2]);
        await NoPlaceIsHeldAsync();
    }

    [Fact]
    public async Task A_stop_naming_a_Thing_whose_only_run_is_queued_stops_that_run()
    {
        Limit(1);
        var first = await StartAsync();
        var about = await StartAsync(Plot);

        await StoppedInTimeAsync(Plot.Id);

        _gateway.RunStatusOf(about).Should().Be(RunStatus.Cancelled);
        _gateway.RunStatusOf(first).Should().Be(RunStatus.Running);
        await FinishAsync(first);
        await NoPlaceIsHeldAsync();
        Begun().Should().Equal(first);
    }

    [Fact]
    public async Task A_stop_that_arrives_while_a_queued_run_is_given_its_place_leaves_no_place_held()
    {
        Limit(1);
        var runs = await StartAsync(3);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateway.BeginHeldUntil = begin.Task;
        AnswerOf(runs[0]).SetResult(Succeeding("echo"));
        await Settle.UntilAsync(() => _gateway.BeginsAsked == 1, "the second run is being given its place");

        var stopping = StoppedInTimeAsync(runs[1]);
        begin.SetResult();
        await stopping;

        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Cancelled);
        await BegunAsync(runs[2]);
        await NothingElseBeginsAsync(runs[0], runs[2]);
        await FinishAsync(runs[2]);
        await NoPlaceIsHeldAsync();
    }

    private Guid Recorded(string status, long? queueNumber = null)
    {
        var runId = Guid.NewGuid();
        _gateway.Runs[runId] = (_pipelineId, default);
        _gateway.RunStatuses[runId] = status;
        if (queueNumber is { } number) _gateway.QueueNumbers[runId] = number;
        return runId;
    }

    [Fact]
    public async Task After_a_restart_queued_runs_begin_in_order_behind_the_runs_carried_on_and_before_a_start_made_since()
    {
        Limit(1);
        var carriedOn = Recorded(RunStatus.Running);
        _gateway.Records[(carriedOn, _drawing.Get("Generate").Id)] = new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>());
        var later = Recorded(RunStatus.Queued, queueNumber: 8);
        var earlier = Recorded(RunStatus.Queued, queueNumber: 5);

        await _executor.RecoverUnfinishedAsync(CancellationToken.None);
        var since = await StartAsync();
        var queuedAgain = await _executor.RecoverQueuedAsync(CancellationToken.None);

        queuedAgain.Should().BeGreaterThanOrEqualTo(2);
        await BegunAsync(carriedOn);
        await NothingElseBeginsAsync(carriedOn);
        foreach (var next in new[] { earlier, later, since })
        {
            await FinishAsync(Begun().Last());
            await BegunAsync(next);
        }
        Begun().Should().Equal(carriedOn, earlier, later, since);
    }

    [Fact]
    public async Task After_a_restart_no_more_queued_runs_begin_than_the_limit_allows()
    {
        Limit(2);
        var queued = new[] { 1, 2, 3 }.Select(number => Recorded(RunStatus.Queued, number)).ToList();

        await _executor.RecoverQueuedAsync(CancellationToken.None);

        await BegunAsync(queued[0]);
        await BegunAsync(queued[1]);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        Begun().Should().BeEquivalentTo(queued.Take(2));
        _gateway.RunStatusOf(queued[2]).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task A_run_a_person_carries_on_is_never_queued_and_holds_a_place()
    {
        Limit(1);
        var stopped = Recorded(RunStatus.Failed);
        _gateway.Records[(stopped, _drawing.Get("Generate").Id)] = new NodeRecord(RunStatus.Interrupted, new Dictionary<string, JsonElement>());
        var going = await StartAsync();

        (await InTimeAsync(_executor.CarryOnAsync(stopped, CancellationToken.None), "the person is answered")).Should().BeNull();

        await BegunAsync(stopped);
        var queued = await StartAsync();
        await FinishAsync(going);
        await NothingElseBeginsAsync(going, stopped);
        await FinishAsync(stopped);
        await BegunAsync(queued);
    }

    [Fact]
    public async Task A_run_a_person_starts_again_is_queued_when_no_place_is_free()
    {
        Limit(1);
        var stopped = Recorded(RunStatus.Failed);
        _gateway.Records[(stopped, _drawing.Get("Generate").Id)] = new NodeRecord(RunStatus.Interrupted, new Dictionary<string, JsonElement>());
        var going = await StartAsync();

        var (startedAgain, refusal) = await _executor.StartAgainAsync(stopped, CancellationToken.None);

        refusal.Should().BeNull();
        _gateway.RunStatusOf(startedAgain!.Value).Should().Be(RunStatus.Queued);
        await FinishAsync(going);
        await BegunAsync(startedAgain.Value);
    }

    [Fact]
    public async Task A_run_started_again_that_could_not_be_recorded_leaves_the_place_it_took()
    {
        Limit(1);
        var stopped = Recorded(RunStatus.Failed);
        _gateway.Records[(stopped, _drawing.Get("Generate").Id)] = new NodeRecord(RunStatus.Interrupted, new Dictionary<string, JsonElement>());
        _gateway.RunCreationFailure = new HttpRequestException("the model refused the run");
        await _executor.StartAgainAsync(stopped, CancellationToken.None);
        _gateway.RunCreationFailure = null;

        var next = await StartAsync();

        _gateway.RunStatusOf(next).Should().Be(RunStatus.Running);
        _gateway.RunStatusOf(await StartAsync()).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task A_queued_run_whose_pipeline_is_deleted_is_recorded_refused_with_the_reason()
    {
        Limit(1);
        var callRecord = Guid.NewGuid();
        var first = await StartAsync();
        var second = await StartAsync(callRecordId: callRecord);
        var withoutThePipeline = new GraphFixture();
        withoutThePipeline.DeclareVocabulary();
        _gateway.FreshGraph = () => withoutThePipeline.Build();

        await FinishAsync(first);

        await StatusAsync(second, RunStatus.Refused);
        _gateway.RunError.Should().Be($"Pipeline {_pipelineId} not found in the loaded subgraph.");
        await Settle.UntilAsync(() => _gateway.ReportsSentSoFar().Count == 2, "both reports reach the broker");
        _gateway.ReportsSentSoFar().Select(sent => sent.Report.Word).Should().Equal(CallReport.Received.Word, CallReport.Failed("reason").Word);
        await NoPlaceIsHeldAsync();
    }

    [Fact]
    public async Task Once_the_limit_is_removed_every_queued_run_begins_at_the_next_finish()
    {
        Limit(1);
        var runs = await StartAsync(3);
        runs.Skip(1).Select(_gateway.RunStatusOf).Should().AllBe(RunStatus.Queued);
        Limit("");

        await FinishAsync(runs[0]);

        await BegunAsync(runs[1]);
        await BegunAsync(runs[2]);
    }

    [Fact]
    public async Task Once_the_limit_is_removed_every_queued_run_begins_at_the_next_start()
    {
        Limit(1);
        var runs = await StartAsync(2);
        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Queued);
        Limit("");

        var next = await StartAsync();

        _gateway.RunStatusOf(next).Should().Be(RunStatus.Running);
        await BegunAsync(runs[1]);
    }

    [Fact]
    public async Task A_lowered_limit_applies_from_the_next_finish()
    {
        Limit(2);
        var runs = await StartAsync(3);
        Limit(1);

        await FinishAsync(runs[0]);
        await NothingElseBeginsAsync(runs[0], runs[1]);
        await FinishAsync(runs[1]);

        await BegunAsync(runs[2]);
    }

    [Fact]
    public async Task A_model_that_cannot_be_read_at_a_queued_runs_turn_is_read_again_until_it_answers()
    {
        Limit(1);
        var runs = await StartAsync(2);
        _gateway.OnRunFinished = () => _gateway.LoadFailure = new HttpRequestException("the broker is away");

        AnswerOf(runs[0]).SetResult(Succeeding("echo"));

        await BegunAsync(runs[1]);

        lock (_pauses) _pauses.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_record_that_cannot_be_read_at_a_queued_runs_turn_is_read_again_until_it_answers()
    {
        Limit(1);
        var runs = await StartAsync(2);
        _gateway.OnRunFinished = () => _gateway.RecordReadFailure = new HttpRequestException("the broker is away");

        AnswerOf(runs[0]).SetResult(Succeeding("echo"));

        await BegunAsync(runs[1]);
        lock (_pauses) _pauses.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_stop_ends_a_queued_run_whose_record_is_never_read_and_its_pause_and_its_turn_passes_to_the_next()
    {
        Limit(1);
        var callRecord = Guid.NewGuid();
        var first = await StartAsync();
        var second = await StartAsync(callRecordId: callRecord);
        var third = await StartAsync();
        await TheModelIsNeverReadAgainAfterAsync(first, failure => _gateway.RecordReadFailure = failure);
        var request = Guid.NewGuid();

        var stopping = _executor.StopAsync(request, second, CancellationToken.None);

        await StatusAsync(second, RunStatus.Cancelled);
        _gateway.RunError.Should().Be("withdrawn by its owner");
        await Settle.UntilAsync(() => Pauses == 2, "the turn passes to the third run, which cannot be read either");
        await FluentActions.Awaiting(() => Settle.ForAsync(stopping, "the stop ends")).Should().ThrowAsync<HttpRequestException>("the stop reads the record as well");
        _gateway.StoppedBy.Should().Equal((second, request));
        _gateway.RunStatusOf(third).Should().Be(RunStatus.Queued);
        await Settle.UntilAsync(() => _gateway.ReportsSentSoFar().Count == 2, "both reports reach the broker");
        _gateway.ReportsSentSoFar().Select(sent => (sent.CallRecordId, sent.Report.Word))
            .Should().Equal((callRecord, CallReport.Received.Word), (callRecord, CallReport.Failed("reason").Word));
    }

    [Fact]
    public async Task A_shutdown_ends_the_pause_of_a_queued_run_whose_record_is_never_read_and_leaves_it_queued_for_the_restart()
    {
        Limit(1);
        var first = await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);
        var third = await StartAsync();
        await TheModelIsNeverReadAgainAfterAsync(first, failure => _gateway.RecordReadFailure = failure);

        _lifetime.StopApplication();

        await Settle.ForAsync(waited, "the pause ends with the shutdown");
        (await waited).Error.Should().Be("The run never began: the orchestrator is stopping.");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        new[] { second, third }.Select(_gateway.RunStatusOf).Should().AllBe(RunStatus.Queued);
        Pauses.Should().Be(1, "no run takes the place of the one that could not begin");
    }

    [Fact]
    public async Task A_shutdown_ends_the_reading_of_a_drawing_that_is_never_read()
    {
        Limit(1);
        var runs = await StartAsync(2);
        var pause = await TheModelIsNeverReadAgainAfterAsync(runs[0], failure => _gateway.LoadFailure = failure);

        _lifetime.StopApplication();
        pause.SetResult();

        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        Pauses.Should().Be(1);
        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task No_queued_run_begins_while_the_orchestrator_is_stopping()
    {
        Limit(1);
        var runs = await StartAsync(2);

        _lifetime.StopApplication();
        await FinishAsync(runs[0]);

        await NothingElseBeginsAsync(runs[0]);
        _gateway.RunStatusOf(runs[1]).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public async Task A_queued_run_that_fails_unexpectedly_as_it_begins_is_logged_and_its_turn_passes_to_the_next()
    {
        Limit(1);
        var runs = await StartAsync(3);
        _gateway.OnRunFinished = () => _gateway.RecordReadFailure = new HttpRequestException("the broker is away");
        _duringAPause = () =>
        {
            _gateway.RecordReadFailure = null;
            throw new InvalidOperationException("no timer can be made");
        };

        AnswerOf(runs[0]).SetResult(Succeeding("echo"));

        await BegunAsync(runs[2]);
        _log.ErrorLines.Should().ContainSingle().Which.Should()
            .StartWith($"Queued run {runs[1]} of pipeline {_pipelineId} failed to run").And.Contain("no timer can be made");
    }

    [Fact]
    public async Task A_queued_run_the_model_refuses_to_begin_is_recorded_refused_and_the_next_takes_its_turn()
    {
        Limit(1);
        var runs = await StartAsync(3);
        _gateway.BeginFailure = new InvalidOperationException("the model refused the status");

        AnswerOf(runs[0]).SetResult(Succeeding("echo"));
        await StatusAsync(runs[1], RunStatus.Refused);
        await StatusAsync(runs[2], RunStatus.Refused);

        _gateway.RunError.Should().Be("the model refused the status");
        Begun().Should().Equal(runs[0]);
        await NoPlaceIsHeldAsync();
    }

    [Fact]
    public async Task A_caller_waiting_for_its_run_waits_for_its_place_as_well()
    {
        Limit(1);
        var first = await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);

        await FinishAsync(first);
        await BegunAsync(second);
        waited.IsCompleted.Should().BeFalse();
        AnswerOf(second).SetResult(Succeeding("echo"));

        (await InTimeAsync(waited, "the caller is answered once its run ends")).Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_whose_queued_run_is_stopped_is_told_when_its_turn_comes_that_it_never_began()
    {
        Limit(1);
        var first = await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);
        await StoppedInTimeAsync(second);

        await FinishAsync(first);

        var result = await InTimeAsync(waited, "the caller is answered at its run's turn");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("The run never began: it was cancelled while it was queued.");
    }

    [Fact]
    public async Task A_caller_that_goes_away_while_its_run_is_queued_leaves_it_cancelled_and_its_turn_passes_to_the_next()
    {
        Limit(1);
        using var caller = new CancellationTokenSource();
        var first = await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, caller.Token, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);
        var third = await StartAsync();

        await caller.CancelAsync();

        await FluentActions.Awaiting(() => Settle.ForAsync(waited, "the caller's wait ends")).Should().ThrowAsync<OperationCanceledException>();
        _gateway.RunStatusOf(second).Should().Be(RunStatus.Cancelled);
        await FinishAsync(first);
        await BegunAsync(third);
        await FinishAsync(third);
        await NoPlaceIsHeldAsync();
        Begun().Should().Equal(first, third);
    }

    [Fact]
    public async Task A_caller_that_goes_away_as_its_run_is_handed_a_place_leaves_that_place()
    {
        Limit(1);
        using var caller = new CancellationTokenSource();
        var first = await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, caller.Token, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);
        var third = await StartAsync();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateway.RecordReadHeldUntil = reading.Task;
        await caller.CancelAsync();
        await Settle.UntilAsync(() => _gateway.RecordReadsHeld == 1, "the caller's going away is being recorded");
        await FinishAsync(first);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        _gateway.RecordReadHeldUntil = Task.CompletedTask;

        reading.SetResult();

        await FluentActions.Awaiting(() => Settle.ForAsync(waited, "the caller's wait ends")).Should().ThrowAsync<OperationCanceledException>();
        _gateway.RunStatusOf(second).Should().Be(RunStatus.Cancelled);
        await BegunAsync(third);
        await NothingElseBeginsAsync(first, third);
        await FinishAsync(third);
        await NoPlaceIsHeldAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caller_that_goes_away_once_its_queued_run_is_stopped_or_gone_from_the_model_writes_nothing_to_it(bool gone)
    {
        Limit(1);
        using var caller = new CancellationTokenSource();
        await StartAsync();
        var second = Guid.NewGuid();
        var waited = Task.Run(() => _executor.RunAsync(_pipelineId, default, caller.Token, second, waitsForItsPlace: true));
        await StatusAsync(second, RunStatus.Queued);
        await StoppedInTimeAsync(second);
        if (gone) _gateway.Runs.Remove(second);
        var written = _gateway.StatusUpdates.Count;

        await caller.CancelAsync();

        await FluentActions.Awaiting(() => Settle.ForAsync(waited, "the caller's wait ends")).Should().ThrowAsync<OperationCanceledException>();
        _gateway.StatusUpdates.Count.Should().Be(written);
    }

    [Fact]
    public async Task A_caller_told_its_run_is_queued_is_answered_at_once()
    {
        Limit(1);
        await StartAsync();

        var queued = await InTimeAsync(_executor.RunAsync(_pipelineId, default, CancellationToken.None), "a queued start is answered at once");

        queued.Success.Should().BeFalse();
        queued.Error.Should().Be("The run is queued: 'Demo' holds 1 run(s) at once.");
        _gateway.RunStatusOf(queued.RunId).Should().Be(RunStatus.Queued);
    }

    [Fact]
    public void A_queued_run_is_not_finished_and_is_reported_to_the_broker_as_received()
    {
        RunStatus.IsFinished(RunStatus.Queued).Should().BeFalse();
        CallReporter.ReportFor(RunStatus.Queued, null).Should().Be(CallReport.Received);
    }
}
