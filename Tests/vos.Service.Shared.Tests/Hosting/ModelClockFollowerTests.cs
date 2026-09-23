using FluentAssertions;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

public class ModelClockFollowerTests
{
    private static readonly DateTimeOffset ModelInstant = new(2025, 9, 20, 3, 0, 0, TimeSpan.Zero);

    // Its own logger, handed to the follower: the static one belongs to every service in the process,
    // and two tests that each swapped it raced over what the other captured.
    private sealed class Captured : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level, EventId eventId, TState state, Exception? failure,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, failure));
    }

    // A clock this test moves by hand, so "has it moved against this machine" is a question about the
    // readings rather than about how long the agent took between two calls.
    private sealed class SteppingClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private static ModelClockFollower NewFollower(
        ModelClock clock, Func<CancellationToken, Task<ModelTimeReading?>> read,
        ILogger? logger = null, string serviceName = "Forage") =>
        new(clock, read, logger ?? new Captured(), serviceName, TimeSpan.FromSeconds(5));

    private static Task FollowOnceAsync(ModelClockFollower follower) =>
        follower.FollowOnceAsync(CancellationToken.None);

    [Fact]
    public async Task A_Reading_Anchors_The_Clock()
    {
        var clock = new ModelClock();
        var follower = NewFollower(clock, _ => Task.FromResult<ModelTimeReading?>(new(ModelInstant, 60)));

        await FollowOnceAsync(follower);

        clock.IsAnchored.Should().BeTrue();
        clock.Rate.Should().Be(60);
        clock.GetUtcNow().Should().BeOnOrAfter(ModelInstant, "the clock runs on from the reading it took")
            .And.BeBefore(ModelInstant.AddDays(1), "from that reading, not from this machine's clock");
    }

    [Fact]
    public async Task A_Broker_That_Cannot_Be_Asked_Leaves_The_Clock_As_It_Was()
    {
        var clock = new ModelClock();
        var follower = NewFollower(clock, _ => Task.FromResult<ModelTimeReading?>(null));

        await FollowOnceAsync(follower);

        clock.IsAnchored.Should().BeFalse();
    }

    [Fact]
    public async Task Anchoring_Says_How_Far_The_Model_Stands_From_This_Machine()
    {
        var captured = new Captured();
        var follower = NewFollower(
            new ModelClock(), _ => Task.FromResult<ModelTimeReading?>(new(ModelInstant, 60)), captured, "Intake");

        await FollowOnceAsync(follower);

        captured.Lines.Should().ContainSingle()
            .Which.Should().Contain("Intake").And.Contain("60").And.Contain(ModelInstant.Year.ToString())
            .And.Contain("behind it by", "a model anchored in the past is behind this machine, not ahead of it");
    }

    [Fact]
    public async Task A_Broker_That_Stays_Away_Is_Said_Once_And_Its_Return_Is_Said_Too()
    {
        var captured = new Captured();
        var answering = false;
        var follower = NewFollower(new ModelClock(), _ => Task.FromResult(
            answering ? new ModelTimeReading(ModelInstant, 1) : null), captured);

        await FollowOnceAsync(follower);
        await FollowOnceAsync(follower);
        await FollowOnceAsync(follower);
        answering = true;
        await FollowOnceAsync(follower);

        captured.Lines.Should().ContainSingle(line => line.Contains("cannot read the model clock"));
        captured.Lines.Should().ContainSingle(line => line.Contains("reading the model clock again"));
    }

    [Fact]
    public async Task A_Clock_That_Has_Not_Moved_Against_This_Machine_Is_Said_Once()
    {
        var captured = new Captured();
        // The same reading twice, with this machine's clock held still between them: the follower says
        // the standing again only where it changed, and whether it changed must not depend on how long
        // the agent took.
        var follower = NewFollower(
            new ModelClock(new SteppingClock(DateTimeOffset.UtcNow)),
            _ => Task.FromResult<ModelTimeReading?>(new(ModelInstant, 1)), captured);

        await FollowOnceAsync(follower);
        await FollowOnceAsync(follower);

        captured.Lines.Should().ContainSingle();
    }
}
