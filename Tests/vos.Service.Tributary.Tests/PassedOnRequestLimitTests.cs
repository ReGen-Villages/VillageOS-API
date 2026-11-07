using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class PassedOnRequestLimitTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public void StartDeadline_WhenTheLimitIsNoLongerThanTheMargin_HasPassedAtOnce()
    {
        var limit = new PassedOnRequestLimit(
            TimeSpan.FromSeconds(1), new FakeTimeProvider(), new CapturingLogger<PassedOnRequestLimit>());

        using var deadline = limit.StartDeadline(CancellationToken.None);

        deadline.HasPassed.Should().BeTrue();
        deadline.Reason("EP").Should().Contain("within 0 of the 1 seconds");
    }

    [Fact]
    public void StartDeadline_WhenTheRequestIsAbortedFirst_DoesNotCountAsTheDeadlinePassing()
    {
        var limit = new PassedOnRequestLimit(Limit, new FakeTimeProvider(), new CapturingLogger<PassedOnRequestLimit>());
        using var aborted = new CancellationTokenSource();

        using var deadline = limit.StartDeadline(aborted.Token);
        aborted.Cancel();

        deadline.Token.IsCancellationRequested.Should().BeTrue();
        deadline.HasPassed.Should().BeFalse();
    }

    [Fact]
    public void WarnOnceWhenNotShorter_PastTheMostEndpointsRemembered_StartsTheRecordAgain()
    {
        var log = new CapturingLogger<PassedOnRequestLimit>();
        var limit = new PassedOnRequestLimit(Limit, new FakeTimeProvider(), log);
        var endpoints = Enumerable.Range(0, PassedOnRequestLimit.MostEndpointsRemembered + 1)
            .Select(_ => Guid.NewGuid()).ToList();

        foreach (var endpoint in endpoints)
            limit.WarnOnceWhenNotShorter(endpoint, "EP", Limit);
        limit.WarnOnceWhenNotShorter(endpoints[^1], "EP", Limit);
        limit.WarnOnceWhenNotShorter(endpoints[0], "EP", Limit);

        log.Lines.Should().HaveCount(endpoints.Count + 1,
            "the last endpoint is remembered after the record starts again, and the first is not");
    }
}
