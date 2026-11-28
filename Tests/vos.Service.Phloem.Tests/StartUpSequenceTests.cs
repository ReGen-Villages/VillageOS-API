using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class StartUpSequenceTests
{
    [Fact]
    public async Task A_registration_refused_for_good_is_one_error_and_the_recovery_still_runs()
    {
        var start = new Start(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        await start.Sequence.RunAsync(CancellationToken.None);

        start.Model.Listed.IsCompleted.Should().BeTrue("the recovery runs although the broker will never take the registration");
        start.ErrorLines.Should().ContainSingle().Which.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task Stopping_while_the_broker_is_still_starting_runs_no_recovery_and_logs_no_error()
    {
        using var stopping = new CancellationTokenSource();
        var start = new Start(_ =>
        {
            stopping.Cancel();
            var stillStarting = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            stillStarting.Headers.Add("Retry-After", "5");
            return stillStarting;
        });
        var queued = Guid.NewGuid();
        await start.Model.CreateRunAsync(queued, start.PipelineId, CancellationToken.None, queueNumber: 1);

        await start.Sequence.RunAsync(stopping.Token);

        start.Model.RunStatusOf(queued).Should().Be(RunStatus.Queued, "an orchestrator that is stopping recovers nothing");
        start.ErrorLines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recovery_that_throws_is_one_error_and_the_sequence_ends()
    {
        var start = new Start(_ => new HttpResponseMessage(HttpStatusCode.OK));
        start.Model.ListingHeldUntil = Task.FromException(new IOException("the model could not be read"));

        await start.Sequence.RunAsync(CancellationToken.None);

        start.ErrorLines.Should().ContainSingle().Which.Should().Contain("the model could not be read");
    }

    [Fact]
    public async Task Each_report_still_owed_and_each_run_still_queued_is_counted_in_the_log()
    {
        var start = new Start(_ => new HttpResponseMessage(HttpStatusCode.OK));
        start.Model.Owed.Add(new OwedReport(Guid.NewGuid(), Guid.NewGuid(), RunStatus.Succeeded, null));
        await start.Model.CreateRunAsync(Guid.NewGuid(), start.PipelineId, CancellationToken.None, queueNumber: 1);

        await start.Sequence.RunAsync(CancellationToken.None);

        start.SequenceLines.Should().Contain(line => line.Contains("Sending again the reports 1 run(s)"))
            .And.Contain(line => line.Contains("Queued 1 run(s) again"));
    }

    private sealed class Start
    {
        private readonly CapturingLogger<MyceliumGateway> _gatewayLog = new();
        private readonly CapturingLogger<StartUpSequence> _sequenceLog = new();

        public Start(Func<HttpRequestMessage, HttpResponseMessage> answerRegistration)
        {
            var (drawing, pipelineId) = TestGraphs.DemoPipeline();
            Model = new FakeGateway(drawing.Build());
            PipelineId = pipelineId;
            var broker = new MyceliumGateway(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(answerRegistration)),
                _gatewayLog, "http://localhost:7243", new ModelClock(), new LaunchToken("svc-token"));
            Sequence = new StartUpSequence(
                broker, new PipelineExecutor(Model, NullLogger<PipelineExecutor>.Instance, new ModelClock(), beginnings: Beginnings), Beginnings,
                _sequenceLog, port: 7300);
        }

        public FakeGateway Model { get; }
        public BeginningGate Beginnings { get; } = new();
        public Guid PipelineId { get; }
        public StartUpSequence Sequence { get; }
        public IReadOnlyList<string> SequenceLines => _sequenceLog.Lines;
        public IEnumerable<string> ErrorLines => _gatewayLog.ErrorLines.Concat(_sequenceLog.ErrorLines);
    }
}
