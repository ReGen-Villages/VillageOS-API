using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// The production starter, which every other test replaces so that a run can be awaited. What it does is
// the part no other test reaches: put the run on the thread pool with a token that outlives the request,
// and keep an exception from escaping a task nothing is awaiting.
public class DiscoveryRunStarterTests
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public readonly CancellationTokenSource Stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => Stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => Stopping.Cancel();
    }

    private sealed class CapturingLogger : ILogger<DiscoveryRunStarter>
    {
        public readonly List<string> Errors = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error) lock (Errors) Errors.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task TheRunIsGivenATokenThatOutlivesTheRequestThatStartedIt()
    {
        var lifetime = new FakeLifetime();
        var ran = new TaskCompletionSource<bool>();
        var starter = new DiscoveryRunStarter(lifetime, new CapturingLogger());

        starter.Start(token =>
        {
            ran.SetResult(token.IsCancellationRequested);
            return Task.CompletedTask;
        });

        (await ran.Task).Should().BeFalse("the request's own token is cancelled as the response completes");
    }

    [Fact]
    public async Task ShuttingDownCancelsARunInFlight()
    {
        var lifetime = new FakeLifetime();
        var observed = new TaskCompletionSource<bool>();
        var starter = new DiscoveryRunStarter(lifetime, new CapturingLogger());
        starter.Start(async token =>
        {
            using var registration = token.Register(() => observed.TrySetResult(true));
            await Task.Delay(Timeout.Infinite, token);
        });

        lifetime.StopApplication();

        (await observed.Task).Should().BeTrue();
    }

    // Nothing awaits the task the starter creates, so an escaping exception is reported by nothing —
    // and on some configurations takes the process down with it.
    [Fact]
    public async Task ARunThatThrows_IsReportedRatherThanEscaping()
    {
        var logger = new CapturingLogger();
        var starter = new DiscoveryRunStarter(new FakeLifetime(), logger);
        var second = new TaskCompletionSource<bool>();

        starter.Start(_ => throw new InvalidOperationException("the model is unreachable"));
        starter.Start(_ => { second.SetResult(true); return Task.CompletedTask; });

        (await second.Task).Should().BeTrue("one run failing does not stop the starter");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (logger.Errors.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        logger.Errors.Should().ContainSingle().Which.Should().Contain("after its dispatch had been accepted");
    }
}
