using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

public class ModelClockFollowerTests
{
    private static readonly DateTimeOffset ModelInstant = new(2025, 9, 20, 3, 0, 0, TimeSpan.Zero);

    private sealed class Captured : ILogEventSink
    {
        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent) => Lines.Add(logEvent.RenderMessage());
    }

    private static ModelClockFollower NewFollower(
        ModelClock clock, Func<CancellationToken, Task<ModelTimeReading?>> read, string serviceName = "Forage") =>
        new(clock, read, serviceName, TimeSpan.FromSeconds(5));

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
        clock.GetUtcNow().Should().BeCloseTo(ModelInstant, TimeSpan.FromSeconds(5));
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
        var before = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        try
        {
            var follower = NewFollower(
                new ModelClock(), _ => Task.FromResult<ModelTimeReading?>(new(ModelInstant, 60)), "Intake");

            await FollowOnceAsync(follower);
        }
        finally
        {
            Log.Logger = before;
        }

        captured.Lines.Should().ContainSingle()
            .Which.Should().Contain("Intake").And.Contain("60").And.Contain(ModelInstant.Year.ToString())
            .And.Contain("behind it by", "a model anchored in the past is behind this machine, not ahead of it");
    }

    [Fact]
    public async Task A_Broker_That_Stays_Away_Is_Said_Once_And_Its_Return_Is_Said_Too()
    {
        var captured = new Captured();
        var before = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        try
        {
            var answering = false;
            var follower = NewFollower(new ModelClock(), _ => Task.FromResult(
                answering ? new ModelTimeReading(ModelInstant, 1) : null));

            await FollowOnceAsync(follower);
            await FollowOnceAsync(follower);
            await FollowOnceAsync(follower);
            answering = true;
            await FollowOnceAsync(follower);
        }
        finally
        {
            Log.Logger = before;
        }

        captured.Lines.Should().ContainSingle(line => line.Contains("cannot read the model clock"));
        captured.Lines.Should().ContainSingle(line => line.Contains("reading the model clock again"));
    }

    [Fact]
    public async Task A_Clock_That_Has_Not_Moved_Against_This_Machine_Is_Said_Once()
    {
        var captured = new Captured();
        var before = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        try
        {
            var follower = NewFollower(
                new ModelClock(), _ => Task.FromResult<ModelTimeReading?>(new(ModelInstant, 1)));

            await FollowOnceAsync(follower);
            await FollowOnceAsync(follower);
        }
        finally
        {
            Log.Logger = before;
        }

        captured.Lines.Should().ContainSingle();
    }
}
