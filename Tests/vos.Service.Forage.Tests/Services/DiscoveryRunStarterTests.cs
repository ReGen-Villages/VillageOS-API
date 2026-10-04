using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Services;
using vos.Tests.Shared;
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
        public readonly List<string> Information = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error) lock (Errors) Errors.Add(formatter(state, exception));
            if (level == LogLevel.Information) lock (Information) Information.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task TheRunIsGivenATokenThatOutlivesTheRequestThatStartedIt()
    {
        var lifetime = new FakeLifetime();
        var ran = new TaskCompletionSource<bool>();
        var starter = new DiscoveryRunStarter(lifetime, new CapturingLogger());

        starter.Start(Guid.NewGuid(), token =>
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
        starter.Start(Guid.NewGuid(), async token =>
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

        starter.Start(Guid.NewGuid(), _ => throw new InvalidOperationException("the model is unreachable"));
        starter.Start(Guid.NewGuid(), _ => { second.SetResult(true); return Task.CompletedTask; });

        (await second.Task).Should().BeTrue("one run failing does not stop the starter");
        await Settle.UntilAsync(() => { lock (logger.Errors) return logger.Errors.Count > 0; },
            "the run that threw is reported");
        logger.Errors.Should().ContainSingle().Which.Should().Contain("after its dispatch had been accepted");
    }

    // A run that could start is given this long to do so before a test concludes it was held.
    private static readonly TimeSpan HeldLongEnough = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan StartsSoonEnough = TimeSpan.FromSeconds(10);

    private static async Task<bool> StartsWithin(Task started, TimeSpan wait) =>
        await Task.WhenAny(started, Task.Delay(wait)) == started;

    [Fact]
    public async Task ASecondRunForTheSameSubject_WaitsForTheFirstToEndAndSaysSo()
    {
        var logger = new CapturingLogger();
        var starter = new DiscoveryRunStarter(new FakeLifetime(), logger);
        var site = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();

        starter.Start(site, async _ => { firstStarted.SetResult(); await releaseFirst.Task; });
        await firstStarted.Task;
        starter.Start(site, _ => { secondStarted.SetResult(); return Task.CompletedTask; });

        (await StartsWithin(secondStarted.Task, HeldLongEnough)).Should().BeFalse(
            "two runs for one site would each mint a coverage for the same call");
        logger.Information.Should().ContainSingle().Which.Should().Contain(site.ToString()).And.Contain("in flight");
        releaseFirst.SetResult();
        (await StartsWithin(secondStarted.Task, StartsSoonEnough)).Should().BeTrue("the held run starts once the first ends");
    }

    [Fact]
    public async Task ARunForASubjectWithNothingInFlight_SaysNothingAboutWaiting()
    {
        var logger = new CapturingLogger();
        var starter = new DiscoveryRunStarter(new FakeLifetime(), logger);
        var ran = new TaskCompletionSource();

        starter.Start(Guid.NewGuid(), _ => { ran.SetResult(); return Task.CompletedTask; });
        await ran.Task;

        logger.Information.Should().BeEmpty();
    }

    [Fact]
    public async Task ADispatchWhileOneRunIsInFlightAndAnotherWaits_IsDroppedAndSaysSo()
    {
        var logger = new CapturingLogger();
        var starter = new DiscoveryRunStarter(new FakeLifetime(), logger);
        var site = Guid.NewGuid();
        var releaseFirst = new TaskCompletionSource();
        var secondEnded = new TaskCompletionSource();
        var thirdRan = false;

        starter.Start(site, _ => releaseFirst.Task);
        starter.Start(site, _ => { secondEnded.SetResult(); return Task.CompletedTask; });
        starter.Start(site, _ => { thirdRan = true; return Task.CompletedTask; });
        releaseFirst.SetResult();
        await secondEnded.Task;
        await Settle.UntilAsync(() => starter.SubjectsWithRunsInFlight == 0, "every run for the site has ended");

        thirdRan.Should().BeFalse(
            "the waiting run reads the model only once the first ends, so it finds whatever the third would have");
        logger.Information.Should().HaveCount(2).And.Contain(line => line.Contains("already waiting"));
    }

    [Fact]
    public async Task ARunStartedAfterTheFirstEnded_StillWaitsForOneQueuedBehindIt()
    {
        var starter = new DiscoveryRunStarter(new FakeLifetime(), new CapturingLogger());
        var site = Guid.NewGuid();
        var releaseFirst = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        var releaseSecond = new TaskCompletionSource();
        var thirdStarted = new TaskCompletionSource();

        starter.Start(site, _ => releaseFirst.Task);
        starter.Start(site, async _ => { secondStarted.SetResult(); await releaseSecond.Task; });
        releaseFirst.SetResult();
        await secondStarted.Task;
        starter.Start(site, _ => { thirdStarted.SetResult(); return Task.CompletedTask; });

        (await StartsWithin(thirdStarted.Task, HeldLongEnough)).Should().BeFalse(
            "the first run ending must not forget the second, which is still in flight");
        releaseSecond.SetResult();
        (await StartsWithin(thirdStarted.Task, StartsSoonEnough)).Should().BeTrue(
            "once the waiting run has started, the next dispatch waits for it rather than being dropped");
    }

    [Fact]
    public async Task RunsForDifferentSubjects_RunAtTheSameTime()
    {
        var starter = new DiscoveryRunStarter(new FakeLifetime(), new CapturingLogger());
        var releaseFirst = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();

        starter.Start(Guid.NewGuid(), _ => releaseFirst.Task);
        starter.Start(Guid.NewGuid(), _ => { secondStarted.SetResult(); return Task.CompletedTask; });

        (await StartsWithin(secondStarted.Task, TimeSpan.FromSeconds(10))).Should().BeTrue(
            "only runs for one subject are held; separate sites are still discovered in parallel");
        releaseFirst.SetResult();
    }

    [Fact]
    public async Task ARunThatThrows_DoesNotHoldTheNextRunForItsSubject()
    {
        var starter = new DiscoveryRunStarter(new FakeLifetime(), new CapturingLogger());
        var site = Guid.NewGuid();
        var secondStarted = new TaskCompletionSource();

        starter.Start(site, _ => throw new InvalidOperationException("the model is unreachable"));
        starter.Start(site, _ => { secondStarted.SetResult(); return Task.CompletedTask; });

        (await StartsWithin(secondStarted.Task, StartsSoonEnough)).Should().BeTrue();
    }

    [Fact]
    public async Task ASubjectWhoseRunsHaveAllEnded_IsNoLongerHeld()
    {
        var starter = new DiscoveryRunStarter(new FakeLifetime(), new CapturingLogger());
        var site = Guid.NewGuid();
        var ended = new TaskCompletionSource();

        starter.Start(site, _ => { ended.SetResult(); return Task.CompletedTask; });
        await ended.Task;

        await Settle.UntilAsync(() => starter.SubjectsWithRunsInFlight == 0,
            "a subject is forgotten once its last run ends, or every site ever discovered stays in memory");
    }
}
