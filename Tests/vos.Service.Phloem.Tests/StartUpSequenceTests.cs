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

        start.Model.QueuedRunsListed.IsCompleted.Should().BeTrue("the recovery runs although the broker will never take the registration");
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

        await start.Sequence.RunAsync(stopping.Token);

        start.Model.QueuedRunsListed.IsCompleted.Should().BeFalse("an orchestrator that is stopping recovers nothing");
        start.ErrorLines.Should().BeEmpty();
    }

    private sealed class Start
    {
        private readonly CapturingLogger<MyceliumGateway> _gatewayLog = new();
        private readonly CapturingLogger<StartUpSequence> _sequenceLog = new();

        public Start(Func<HttpRequestMessage, HttpResponseMessage> answerRegistration)
        {
            Model = new FakeGateway(TestGraphs.DemoPipeline().Fixture.Build());
            var broker = new MyceliumGateway(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(answerRegistration)),
                _gatewayLog, "http://localhost:7243", new ModelClock(), new LaunchToken("svc-token"));
            Sequence = new StartUpSequence(
                broker, new PipelineExecutor(Model, NullLogger<PipelineExecutor>.Instance, new ModelClock()), _sequenceLog, port: 7300);
        }

        public FakeGateway Model { get; }
        public StartUpSequence Sequence { get; }
        public IEnumerable<string> ErrorLines => _gatewayLog.ErrorLines.Concat(_sequenceLog.ErrorLines);
    }
}
