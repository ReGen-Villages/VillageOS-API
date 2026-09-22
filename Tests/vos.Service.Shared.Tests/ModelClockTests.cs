using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class ModelClockTests
{
    private static readonly DateTimeOffset WallClock = new(2026, 9, 20, 22, 49, 34, TimeSpan.Zero);
    private static readonly DateTimeOffset ModelInstant = new(2025, 9, 20, 3, 0, 0, TimeSpan.Zero);

    private sealed class SteppingClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    [Fact]
    public void Unanchored_It_Is_The_Wall_Clock()
    {
        var clock = new ModelClock(new SteppingClock(WallClock));

        clock.IsAnchored.Should().BeFalse();
        clock.GetUtcNow().Should().Be(WallClock);
        clock.Rate.Should().Be(1.0);
        clock.OffsetFromWallClock().Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Anchored_It_Runs_From_The_Model_Instant_At_The_Rate_It_Was_Given()
    {
        var real = new SteppingClock(WallClock);
        var clock = new ModelClock(real);

        clock.AnchorTo(ModelInstant, rate: 60);
        real.Advance(TimeSpan.FromSeconds(30));

        clock.GetUtcNow().Should().Be(ModelInstant.AddMinutes(30));
        clock.Rate.Should().Be(60);
    }

    [Fact]
    public void The_Offset_Is_How_Far_The_Model_Stands_From_This_Machine()
    {
        var clock = new ModelClock(new SteppingClock(WallClock));

        clock.AnchorTo(ModelInstant, rate: 60);

        clock.OffsetFromWallClock().Should().Be(ModelInstant - WallClock);
    }

    [Fact]
    public void A_Clock_Running_Backwards_Is_Refused()
    {
        var clock = new ModelClock(new SteppingClock(WallClock));

        var refused = () => clock.AnchorTo(ModelInstant, rate: -1);

        refused.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Reset_Puts_It_Back_On_The_Wall_Clock()
    {
        var clock = new ModelClock(new SteppingClock(WallClock));
        clock.AnchorTo(ModelInstant, rate: 60);

        clock.Reset();

        clock.GetUtcNow().Should().Be(WallClock);
        clock.IsAnchored.Should().BeFalse();
    }
}
