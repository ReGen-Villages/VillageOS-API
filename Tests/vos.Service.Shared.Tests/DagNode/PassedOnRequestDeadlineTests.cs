using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Shared.DagNode;
using Xunit;

namespace vos.Service.Shared.Tests.DagNode;

public class PassedOnRequestDeadlineTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public void StartDeadline_WhenTheLimitIsShorterThanTheMargin_TakesTheWholeLimit()
    {
        var clock = new FakeTimeProvider();
        var limit = new PassedOnRequestLimit(TimeSpan.FromSeconds(1), clock);

        using var deadline = limit.StartDeadline(CancellationToken.None);
        deadline.HasPassed.Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));

        deadline.HasPassed.Should().BeTrue();
        deadline.Allowed.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StartDeadline_WhenTheRequestIsAbortedFirst_DoesNotCountAsTheDeadlinePassing()
    {
        var limit = new PassedOnRequestLimit(Limit, new FakeTimeProvider());
        using var aborted = new CancellationTokenSource();

        using var deadline = limit.StartDeadline(aborted.Token);
        aborted.Cancel();

        deadline.Token.IsCancellationRequested.Should().BeTrue();
        deadline.HasPassed.Should().BeFalse();
    }

    [Fact]
    public void The_time_left_is_the_limit_less_the_margin_less_the_time_since_the_start_and_never_below_nothing()
    {
        var clock = new FakeTimeProvider();
        using var deadline = new PassedOnRequestLimit(Limit, clock).StartDeadline(CancellationToken.None);

        deadline.TimeLeft.Should().Be(Limit - PassedOnRequestDeadline.MarginForTheAnswer);
        clock.Advance(TimeSpan.FromSeconds(5));
        deadline.TimeLeft.Should().Be(TimeSpan.FromSeconds(23));
        clock.Advance(TimeSpan.FromSeconds(60));
        deadline.TimeLeft.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void With_no_limit_there_is_no_time_left_to_count_and_the_token_is_the_requests_own()
    {
        using var aborted = new CancellationTokenSource();

        using var deadline = new PassedOnRequestLimit(null, new FakeTimeProvider()).StartDeadline(aborted.Token);

        deadline.TimeLeft.Should().BeNull();
        deadline.Token.Should().Be(aborted.Token);
    }
}
