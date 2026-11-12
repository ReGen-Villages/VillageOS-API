using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

public class ModelClockFollowerTests
{
    private static readonly DateTimeOffset ModelInstant = new(2025, 9, 20, 3, 0, 0, TimeSpan.Zero);

    private sealed class Captured : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level, EventId eventId, TState state, Exception? failure,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, failure));
    }

    private static async IAsyncEnumerable<ModelTimeReading> Announcing(
        IEnumerable<ModelTimeReading> readings, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        foreach (var reading in readings)
        {
            await Task.Yield();
            yield return reading;
        }
    }

    private static Func<CancellationToken, IAsyncEnumerable<ModelTimeReading>> Broker(params ModelTimeReading[] readings) =>
        cancellation => Announcing(readings, cancellation);

    private static ModelClockFollower NewFollower(
        ModelClock clock, Func<CancellationToken, IAsyncEnumerable<ModelTimeReading>> listen,
        ILogger? logger = null, string serviceName = "Forage", TimeSpan? reconnectDelay = null) =>
        new(clock, listen, logger ?? new Captured(), serviceName, reconnectDelay ?? TimeSpan.FromSeconds(2));

    private static Task ListenOnceAsync(ModelClockFollower follower) =>
        follower.ListenOnceAsync(CancellationToken.None);

    [Fact]
    public async Task The_Reading_Taken_On_Connecting_Anchors_The_Clock()
    {
        var clock = new ModelClock();
        var follower = NewFollower(clock, Broker(new ModelTimeReading(ModelInstant, 60)));

        await ListenOnceAsync(follower);

        clock.IsAnchored.Should().BeTrue();
        clock.Rate.Should().Be(60);
        clock.GetUtcNow().Should().BeOnOrAfter(ModelInstant, "the clock runs on from the reading it took")
            .And.BeBefore(ModelInstant.AddDays(1), "from that reading, not from this machine's clock");
    }

    [Fact]
    public async Task An_Announced_Change_Re_Anchors_The_Clock()
    {
        var clock = new ModelClock();
        var follower = NewFollower(clock, Broker(new ModelTimeReading(ModelInstant, 60), new ModelTimeReading(ModelInstant.AddHours(1), 300)));

        await ListenOnceAsync(follower);

        clock.Rate.Should().Be(300);
        clock.GetUtcNow().Should().BeOnOrAfter(ModelInstant.AddHours(1), "the clock runs on from the announced instant");
    }

    [Fact]
    public async Task A_Broker_That_Cannot_Be_Reached_Leaves_The_Clock_As_It_Was()
    {
        var clock = new ModelClock();
        var follower = NewFollower(clock, Broker());

        await ListenOnceAsync(follower);

        clock.IsAnchored.Should().BeFalse();
    }

    [Fact]
    public async Task Anchoring_Says_How_Far_The_Model_Stands_From_This_Machine()
    {
        var captured = new Captured();
        var follower = NewFollower(new ModelClock(), Broker(new ModelTimeReading(ModelInstant, 60)), captured, "Intake");

        await ListenOnceAsync(follower);

        captured.Lines.Should().ContainSingle()
            .Which.Should().Contain("Intake").And.Contain("60").And.Contain(ModelInstant.Year.ToString())
            .And.Contain("behind it by", "a model anchored in the past is behind this machine, not ahead of it");
    }

    [Fact]
    public async Task A_Broker_That_Stays_Away_Is_Said_Once_And_Its_Return_Is_Said_Too()
    {
        var captured = new Captured();
        var answering = false;
        var follower = NewFollower(new ModelClock(),
            cancellation => Announcing(answering ? [new ModelTimeReading(ModelInstant, 1)] : [], cancellation), captured);

        await ListenOnceAsync(follower);
        await ListenOnceAsync(follower);
        await ListenOnceAsync(follower);
        answering = true;
        await ListenOnceAsync(follower);

        captured.Lines.Should().ContainSingle(line => line.Contains("cannot read the model clock"));
        captured.Lines.Should().ContainSingle(line => line.Contains("reading the model clock again"));
    }

    [Fact]
    public async Task A_Clock_That_Has_Not_Moved_Against_This_Machine_Is_Said_Once()
    {
        var captured = new Captured();
        // The same reading on two connections, with this machine's clock held still between them: the
        // follower says the standing again only where it changed, and whether it changed must not depend
        // on how long the agent took.
        var follower = NewFollower(
            new ModelClock(new FakeTimeProvider(DateTimeOffset.UtcNow)), Broker(new ModelTimeReading(ModelInstant, 1)), captured);

        await ListenOnceAsync(follower);
        await ListenOnceAsync(follower);

        captured.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Stream_That_Ends_Is_Opened_Again_After_The_Reconnect_Delay()
    {
        var connections = 0;
        var follower = NewFollower(new ModelClock(),
            cancellation => Announcing(Interlocked.Increment(ref connections) > 0 ? [new ModelTimeReading(ModelInstant, 1)] : [], cancellation),
            reconnectDelay: TimeSpan.FromMilliseconds(10));

        await follower.StartAsync(CancellationToken.None);
        try
        {
            await Settle.UntilAsync(() => Volatile.Read(ref connections) >= 3, "the follower listens again each time the stream ends");
        }
        finally
        {
            await follower.StopAsync(CancellationToken.None);
        }
    }
}
