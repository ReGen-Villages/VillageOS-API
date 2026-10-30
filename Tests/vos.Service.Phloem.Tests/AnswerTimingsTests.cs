using FluentAssertions;
using vos.Service.Phloem.Execution;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class AnswerTimingsTests
{
    [Fact]
    public async Task Each_step_is_named_with_its_time_in_the_order_it_ran()
    {
        var timings = new AnswerTimings();

        await timings.TimeAsync("checking for a stop", () => Task.FromResult(false));
        timings.Record("waiting for the placing lock", 250);

        timings.Steps.Should().MatchRegex(@"^checking for a stop \d+ ms, waiting for the placing lock 250 ms$");
    }

    [Fact]
    public async Task A_step_that_throws_is_still_timed()
    {
        var timings = new AnswerTimings();

        var failing = () => timings.TimeAsync<bool>("checking for a stop", () => throw new HttpRequestException("timed out"));

        await failing.Should().ThrowAsync<HttpRequestException>();
        timings.Steps.Should().MatchRegex(@"^checking for a stop \d+ ms$");
    }
}
