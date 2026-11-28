using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Shared.DagNode;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class TributaryRequestDeadlineTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public void Reason_WhenTheLimitIsShorterThanTheMargin_NamesTheWholeLimit()
    {
        using var deadline = new PassedOnRequestLimit(TimeSpan.FromSeconds(1), new FakeTimeProvider()).StartDeadline(CancellationToken.None);

        new TributaryRequestDeadline(deadline).Reason("EP").Should().Contain("within 1 of the 1 seconds");
    }

    [Fact]
    public void WarnOnceWhenNotShorter_PastTheMostEndpointsRemembered_StartsTheRecordAgain()
    {
        var log = new CapturingLogger<PassedOnRequestLimitWarnings>();
        var warnings = new PassedOnRequestLimitWarnings(new PassedOnRequestLimit(Limit, new FakeTimeProvider()), log);
        var endpoints = Enumerable.Range(0, PassedOnRequestLimitWarnings.MostEndpointsRemembered + 1)
            .Select(_ => Guid.NewGuid()).ToList();

        foreach (var endpoint in endpoints)
            warnings.WarnOnceWhenNotShorter(endpoint, "EP", Limit);
        warnings.WarnOnceWhenNotShorter(endpoints[^1], "EP", Limit);
        warnings.WarnOnceWhenNotShorter(endpoints[0], "EP", Limit);

        log.Lines.Should().HaveCount(endpoints.Count + 1,
            "the last endpoint is remembered after the record starts again, and the first is not");
    }
}
