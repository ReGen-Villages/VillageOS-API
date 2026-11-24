using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Auth;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Service.Shared.ServiceCalls;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A call's token lasts minutes, and a run goes on for as long as its steps take. Generate, the first node of every run
// here, answers when the test says so.
public class RunTokenTests
{
    private static readonly Guid Model = Guid.NewGuid();
    private readonly GraphFixture _drawing;
    private readonly Guid _pipelineId;
    private readonly FakeGateway _gateway;
    private readonly PipelineExecutor _executor;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<NodeCallResult>> _answers = new();
    private readonly ConcurrentDictionary<Guid, string?> _calledUnder = new();

    public RunTokenTests()
    {
        (_drawing, _pipelineId) = TestGraphs.DemoPipeline();
        _drawing.State(_drawing.Get("Demo"), ModelNames.RunsAtOnce, 1);
        _gateway = new FakeGateway(_drawing.Build()) { FreshGraph = () => _drawing.Build(), OnCallAsync = CallAsync };
        _executor = new PipelineExecutor(_gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
    }

    private Task<NodeCallResult> CallAsync(string subdomain, JsonElement envelope)
    {
        var runId = envelope.GetProperty("runId").GetGuid();
        if (subdomain != "gen") return Task.FromResult(Succeeding);
        _calledUnder[runId] = MyceliumModelToken.Current;
        return AnswerOf(runId).Task;
    }

    private TaskCompletionSource<NodeCallResult> AnswerOf(Guid runId) =>
        _answers.GetOrAdd(runId, _ => new TaskCompletionSource<NodeCallResult>(TaskCreationOptions.RunContinuationsAsynchronously));

    private static NodeCallResult Succeeding => new(200, """{"result":"success","outputs":{"echo":"x"}}""");

    private static async Task<(HeldModelToken Held, string Token)> HoldAsync()
    {
        var token = TestTokens.For(Model, scope: $"endpoint:test:{Guid.NewGuid():N}");
        HeldModelToken? held = null;
        await MyceliumModelToken.ActingForAsync(TestTokens.For(Model, DateTimeOffset.UtcNow.AddMinutes(5)), async () =>
            held = await HeldModelToken.SwapFromTheCallAsync(new ExchangeAnswering(token), NullLogger.Instance));
        return (held!, token);
    }

    private async Task<Guid> StartAsync(HeldModelToken held)
    {
        var runId = Guid.NewGuid();
        _ = Task.Run(() => held.ActAsync(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, runId)));
        await Settle.UntilAsync(() => _gateway.RunStatusOf(runId) is not null, "the run is recorded");
        return runId;
    }

    private async Task FinishAsync(Guid runId)
    {
        await Settle.UntilAsync(() => _calledUnder.ContainsKey(runId), "the run begins");
        AnswerOf(runId).TrySetResult(Succeeding);
        await Settle.UntilAsync(() => _gateway.RunStatusOf(runId) == RunStatus.Succeeded, "the run succeeds");
    }

    private IEnumerable<string?> WrittenUnder(Guid runId) =>
        _gateway.WrittenUnder.Where(write => write.RunId == runId).Select(write => write.Token).Distinct();

    [Fact]
    public async Task Bug8737_A_queued_run_begun_by_another_runs_end_goes_on_under_its_own_token()
    {
        var (held, token) = await HoldAsync();
        var (heldByTheQueued, tokenOfTheQueued) = await HoldAsync();
        var first = await StartAsync(held);
        var queued = await StartAsync(heldByTheQueued);
        _gateway.RunStatusOf(queued).Should().Be(RunStatus.Queued);

        await FinishAsync(first);
        await FinishAsync(queued);

        _calledUnder[queued].Should().Be(tokenOfTheQueued);
        WrittenUnder(queued).Should().Equal(tokenOfTheQueued);
        WrittenUnder(first).Should().Equal(token);
    }

    [Fact]
    public async Task Bug8737_A_queued_run_recovered_after_a_restart_never_sees_the_token_of_the_run_that_hands_over_to_it()
    {
        var (held, _) = await HoldAsync();
        var first = await StartAsync(held);
        var recovered = Guid.NewGuid();
        _gateway.Runs[recovered] = (_pipelineId, default);
        _gateway.QueueNumbers[recovered] = 1;
        _gateway.RunStatuses[recovered] = RunStatus.Queued;
        await _executor.RecoverQueuedAsync(CancellationToken.None);

        await FinishAsync(first);
        await FinishAsync(recovered);

        _calledUnder[recovered].Should().BeNull();
        WrittenUnder(recovered).Should().Equal((string?)null);
    }

    [Fact]
    public async Task Bug8737_A_queued_run_whose_call_carried_no_token_never_sees_the_token_of_the_run_that_hands_over_to_it()
    {
        var (held, _) = await HoldAsync();
        var first = await StartAsync(held);
        var queued = await StartAsync(HeldModelToken.None);

        await FinishAsync(first);
        await FinishAsync(queued);

        _calledUnder[queued].Should().BeNull();
        WrittenUnder(queued).Should().Equal((string?)null);
    }

    [Fact]
    public async Task Bug8737_A_waited_start_whose_caller_goes_away_while_it_is_queued_is_cancelled_under_its_own_token()
    {
        var (held, _) = await HoldAsync();
        var (heldByTheWaited, tokenOfTheWaited) = await HoldAsync();
        var first = await StartAsync(held);
        using var caller = new CancellationTokenSource();
        var waited = Guid.NewGuid();
        _ = Task.Run(() => heldByTheWaited.ActAsync(() => _executor.RunAsync(_pipelineId, default, caller.Token, waited, waitsForItsPlace: true)));
        await Settle.UntilAsync(() => _gateway.RunStatusOf(waited) == RunStatus.Queued, "the waited run is queued");

        await caller.CancelAsync();

        await Settle.UntilAsync(() => _gateway.RunStatusOf(waited) == RunStatus.Cancelled, "the waited run is cancelled");
        WrittenUnder(waited).Should().Equal(tokenOfTheWaited);
        await FinishAsync(first);
        _calledUnder.Should().NotContainKey(waited);
        await Settle.UntilAsync(() => _executor.PipelinesHoldingPlaces == 0, "no place is held");
    }

    [Fact]
    public async Task Bug8737_A_queued_run_a_node_started_begins_under_the_token_of_the_run_that_started_it()
    {
        var starting = TestGraphs.StartingPipeline();
        var drawing = starting.Fixture;
        var next = drawing.Get("What follows");
        drawing.State(next, ModelNames.RunsAtOnce, 1);
        var begins = drawing.Thing("What follows begins");
        drawing.Rel(begins, drawing.Get("is"), drawing.Get("PipelineInput"));
        var port = drawing.Thing("begins.result", ("direction", "out"), ("type", "any"), ("portName", "result"));
        drawing.Rel(port, drawing.Get("is"), drawing.Get("Port"));
        drawing.Rel(begins, drawing.Get("has"), port);
        drawing.Rel(next, drawing.Get("has"), begins);
        drawing.EndEveryPath(next);
        var gateway = new FakeGateway(drawing.Build()) { FreshGraph = drawing.Build, OnCall = (_, _) => Succeeding };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var (heldByAnother, _) = await HoldAsync();
        var (held, token) = await HoldAsync();
        var recordingOfTheOther = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.RunCreationHeldUntil = recordingOfTheOther.Task;
        _ = Task.Run(() => heldByAnother.ActAsync(() =>
            executor.RunAsync(next.Id, JsonSerializer.SerializeToElement(new { result = "x" }), CancellationToken.None)));
        await Settle.UntilAsync(() => gateway.RunCreationStarted, "another run holds the only place of what follows");
        gateway.RunCreationHeldUntil = Task.CompletedTask;

        await held.ActAsync(() => executor.RunAsync(starting.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None));

        await Settle.UntilAsync(() => { lock (gateway.RunsCreated) return gateway.RunsCreated.Count == 2; }, "the started run is recorded");
        Guid started;
        lock (gateway.RunsCreated) started = gateway.RunsCreated[1].RunId;
        gateway.RunStatusOf(started).Should().Be(RunStatus.Queued);
        recordingOfTheOther.SetResult();
        await Settle.UntilAsync(() => gateway.RunStatusOf(started) == RunStatus.Succeeded, "the started run succeeds");
        gateway.WrittenUnder.Where(write => write.RunId == started).Select(write => write.Token).Distinct().Should().Equal(token);
    }

    [Fact]
    public async Task Bug8737_A_queued_run_whose_token_is_due_and_cannot_be_replaced_leaves_its_place_to_the_next()
    {
        var (held, _) = await HoldAsync();
        var exchange = new ExchangeTimingOutOnReplacement(TestTokens.For(Model, DateTimeOffset.UtcNow.AddMinutes(30)));
        HeldModelToken? due = null;
        await MyceliumModelToken.ActingForAsync(TestTokens.For(Model, DateTimeOffset.UtcNow.AddMinutes(5)), async () =>
            due = await HeldModelToken.SwapFromTheCallAsync(exchange, NullLogger.Instance));
        var first = await StartAsync(held);
        var stuck = Guid.NewGuid();
        exchange.TimesOut = false;
        _ = Task.Run(() => due!.ActAsync(() => _executor.RunAsync(_pipelineId, default, CancellationToken.None, stuck)));
        await Settle.UntilAsync(() => _gateway.RunStatusOf(stuck) == RunStatus.Queued, "the run is queued");
        exchange.TimesOut = true;
        var next = await StartAsync(held);

        await FinishAsync(first);
        await FinishAsync(next);

        _calledUnder.Should().NotContainKey(stuck);
    }

    private sealed class ExchangeTimingOutOnReplacement(string token) : IServiceTokenExchange
    {
        public bool TimesOut { get; set; }

        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default) =>
            TimesOut ? Task.FromException<ModelScopedBearer?>(new TaskCanceledException("the broker did not answer in time"))
                : Task.FromResult(ModelScopedBearer.Read(token));
    }

    public enum Call { Start, StartWaited, RelationshipStart, StartAgain, CarryOn, AnswerToAWait, Stop }

    public static TheoryData<Call> EveryCall() => new(Enum.GetValues<Call>());

    [Theory]
    [MemberData(nameof(EveryCall))]
    public async Task Bug8737_What_a_call_leaves_behind_goes_on_under_a_token_swapped_for_the_calls_own(Call call)
    {
        var swapped = TestTokens.For(Model, scope: "endpoint:phloem:swapped");
        var exchange = new ExchangeAnswering(swapped);
        await using var service = new AuthenticatedPhloem(exchange);

        using var response = await service.MakeAsync(call);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await service.EndedAsync();
        service.WrittenSinceTheCall().Should().NotBeEmpty().And.OnlyContain(write => write.Token == swapped);
        exchange.Presented.Should().Equal(service.CallToken);
    }

    [Theory]
    [MemberData(nameof(EveryCall))]
    public async Task Bug8737_A_call_whose_token_the_broker_will_not_swap_is_refused_and_leaves_nothing_behind(Call call)
    {
        await using var service = new AuthenticatedPhloem(new ExchangeAnswering(null));

        using var response = await service.MakeAsync(call);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        service.WrittenSinceTheCall().Should().BeEmpty();
        service.PlacesHeld.Should().Be(0);
    }

    [Fact]
    public async Task Bug8739_A_test_call_arranges_nothing_while_the_recovery_at_start_is_under_way()
    {
        await using var service = new AuthenticatedPhloem(new ExchangeAnswering(null));
        var recoveryLetGo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Gateway.WaitsListingHeldUntil = recoveryLetGo.Task;
        var arranged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Gateway.OnCallAsync = (subdomain, envelope) =>
        {
            arranged.TrySetResult();
            return Task.FromResult(service.Gateway.OnCall(subdomain, envelope));
        };

        var making = Task.Run(() => service.MakeAsync(Call.Stop));
        await Settle.ForAsync(Task.WhenAny(service.WaitsForTheRecovery, arranged.Task), "the call waits for the recovery, or arranges its run");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));

        arranged.Task.IsCompleted.Should().BeFalse("no run is arranged while the recovery is held");
        service.Gateway.RunCreationStarted.Should().BeFalse();
        recoveryLetGo.SetResult();
        await Settle.ForAsync(making, "the refused call answers");
        using var response = await making;
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        service.WrittenSinceTheCall().Should().BeEmpty();
    }

    [Fact]
    public async Task A_call_that_carries_no_token_asks_for_no_swap_and_its_run_presents_the_services_own_credential()
    {
        var exchange = new ExchangeAnswering("never asked for");
        await using var service = new AuthenticatedPhloem(exchange, authenticated: false);

        using var response = await service.MakeAsync(Call.StartWaited);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        exchange.Presented.Should().BeEmpty();
        service.WrittenSinceTheCall().Should().NotBeEmpty().And.OnlyContain(write => write.Token == null);
    }

    [Fact]
    public async Task A_call_whose_body_names_nothing_to_do_is_refused_before_its_token_is_swapped()
    {
        var exchange = new ExchangeAnswering("never asked for");
        await using var service = new AuthenticatedPhloem(exchange);

        using var response = await service.PostAsync(new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        exchange.Presented.Should().BeEmpty();
    }

    private sealed class ExchangeAnswering(string? token) : IServiceTokenExchange
    {
        public ConcurrentQueue<string> Presented { get; } = new();

        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default)
        {
            Presented.Enqueue(bearer);
            return Task.FromResult(ModelScopedBearer.Read(token));
        }
    }

    private sealed class AuthenticatedPhloem : WebApplicationFactory<Program>
    {
        private const string Issuer = "VillageOS";
        private const string Audience = "phloem-handler";

        private readonly MyceliumSigner _signer = new();
        private readonly IServiceTokenExchange _exchange;
        private readonly bool _authenticated;
        private readonly TestGraphs.WaitingFixture _waiting = TestGraphs.WaitingPipeline();
        private readonly RunSubject _plot;
        private readonly FakeGateway _gateway;
        private int _writtenBefore;

        public AuthenticatedPhloem(IServiceTokenExchange exchange, bool authenticated = true)
        {
            _exchange = exchange;
            _authenticated = authenticated;
            CallToken = _signer.Token(Issuer, Audience, new Claim(VosClaims.ModelId, Model.ToString()));
            _waiting.Fixture.State(_waiting.Fixture.Get("Review a submission"), ModelNames.RunsAtOnce, 1);
            _plot = new RunSubject(_waiting.Fixture.Thing("Plot 7").Id, "Plot 7");
            _gateway = new FakeGateway(_waiting.Fixture.Build())
            {
                FreshGraph = () => _waiting.Fixture.Build(),
                OnCall = (subdomain, _) => new NodeCallResult(200,
                    subdomain == "review" ? """{"result":"success","outputs":{"notice":"sent"}}""" : """{"result":"success","outputs":{"out":"x"}}"""),
            };
        }

        public string CallToken { get; }

        public int PlacesHeld => Services.GetRequiredService<PipelineExecutor>().PipelinesHoldingPlaces;

        public FakeGateway Gateway => _gateway;

        private readonly TaskCompletionSource _waitsForTheRecovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitsForTheRecovery => _waitsForTheRecovery.Task;

        public IEnumerable<(Guid RunId, string? Token)> WrittenSinceTheCall() => _gateway.WrittenUnder.Skip(_writtenBefore);

        public Task EndedAsync() => Settle.UntilAsync(() =>
        {
            lock (_gateway.StatusUpdates)
                return _gateway.StatusUpdates.Count > 0
                    && _gateway.StatusUpdates[^1] is RunStatus.Waiting or RunStatus.Succeeded or RunStatus.Cancelled;
        }, "what the call left behind is written");

        public async Task<HttpResponseMessage> MakeAsync(Call call)
        {
            await UntilTheRecoveryAtStartHasMadeItsLastListingAsync();
            var body = await BodyOfAsync(call);
            lock (_gateway.StatusUpdates) _gateway.StatusUpdates.Clear();
            _writtenBefore = _gateway.WrittenUnder.Count;
            return await PostAsync(body);
        }

        // A run arranged earlier can be listed by the recovery, which then sweeps it again after the call.
        private async Task UntilTheRecoveryAtStartHasMadeItsLastListingAsync()
        {
            StartServer();
            _waitsForTheRecovery.TrySetResult();
            await Settle.ForAsync(_gateway.QueuedRunsListed, "the recovery at start lists the queued runs");
        }

        public async Task<HttpResponseMessage> PostAsync(object body)
        {
            using var client = CreateClient();
            if (_authenticated) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CallToken);
            return await client.PostAsync("/handle", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        }

        private async Task<object> BodyOfAsync(Call call)
        {
            switch (call)
            {
                case Call.Start:
                    return new { pipelineId = _waiting.PipelineId, parameters = About(), async = true };
                case Call.StartWaited:
                    return new { pipelineId = _waiting.PipelineId, parameters = About() };
                case Call.RelationshipStart:
                    return new { relationshipId = Guid.NewGuid(), subjectId = _plot.Id, subjectName = _plot.Name, targetId = _waiting.PipelineId, properties = new { } };
                case Call.StartAgain:
                    return new { startAgain = StoppedByARestart() };
                case Call.CarryOn:
                    return new { carryOn = StoppedByARestart() };
                case Call.AnswerToAWait:
                    return new { targetId = _gateway.NodeRunIdOf(await WaitingRunAsync(), _waiting.Review.Id), properties = new { } };
                default:
                    var stop = Guid.NewGuid();
                    _gateway.PredicatesByRelationship[stop] = _gateway.StopsConnection;
                    return new { relationshipId = stop, subjectId = Guid.NewGuid(), targetId = await WaitingRunAsync(), properties = new { } };
            }
        }

        private object About() => new Dictionary<string, object> { [PipelineExecutor.SubjectParam] = _plot.Id };

        private async Task<Guid> WaitingRunAsync() => (await Services.GetRequiredService<PipelineExecutor>()
            .RunAsync(_waiting.PipelineId, default, CancellationToken.None, subject: _plot)).RunId;

        private Guid StoppedByARestart()
        {
            var runId = Guid.NewGuid();
            _gateway.Runs[runId] = (_waiting.PipelineId, JsonSerializer.SerializeToElement(new { subject = new { id = _plot.Id, name = _plot.Name } }));
            _gateway.RunStatuses[runId] = RunStatus.Failed;
            _gateway.Records[(runId, _waiting.Fixture.Get("A submission arrives").Id)] = new NodeRecord(RunStatus.Succeeded,
                new Dictionary<string, JsonElement> { ["subject"] = JsonSerializer.SerializeToElement(new { id = _plot.Id, name = _plot.Name }) });
            _gateway.Records[(runId, _waiting.Review.Id)] = new NodeRecord(RunStatus.Interrupted, new Dictionary<string, JsonElement>());
            return runId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Port", "5000");
            builder.UseSetting("MyceliumUrl", "http://localhost");
            builder.UseSetting("Token", "test-token");
            if (_authenticated)
            {
                builder.UseSetting("VerificationKey", _signer.VerificationKey);
                builder.UseSetting("Issuer", Issuer);
                builder.UseSetting("Audience", Audience);
            }
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
                services.RemoveAll<IMyceliumGateway>();
                services.AddSingleton<IMyceliumGateway>(_gateway);
                services.RemoveAll<IServiceTokenExchange>();
                services.AddSingleton(_exchange);
            });
        }
    }
}
