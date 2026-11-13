using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Tributary.Services;
using Xunit;

namespace vos.Service.Tributary.Tests;

public class StepAnswerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonElement Call = JsonSerializer.SerializeToElement(new
    {
        endpointName = "EP",
        subjectId = Guid.Parse("8202aaaa-0000-0000-0000-000000000001"),
        addressParameters = new { lat = "39.4" },
    });

    private static StepAnswer Answered(EndpointCallResult result) => StepAnswer.From(result, Call, Now);

    [Fact]
    public void A_call_that_wrote_readings_succeeds_with_the_call_and_what_it_wrote()
    {
        var written = new Dictionary<string, object?> { ["flowRegime"] = "steady" };

        var answer = Answered(EndpointCallResult.Ingested(new IngestSummary(Guid.NewGuid(), 1, 1, written)));

        answer.Succeeded.Should().BeTrue();
        answer.Outcome[StepAnswer.CallField].Should().Be(Call);
        answer.Outcome[StepAnswer.WrittenField].Should().BeSameAs(written);
        answer.Reason.Should().BeNull();
    }

    [Fact]
    public void A_call_answered_with_a_body_succeeds_having_written_nothing()
    {
        var answer = Answered(EndpointCallResult.Body("""{"anything":1}""", "application/json"));

        answer.Succeeded.Should().BeTrue();
        answer.Outcome.Should().ContainKey(StepAnswer.WrittenField).WhoseValue.Should().BeNull();
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void A_provider_answer_that_can_change_fails_and_may_be_tried_again_in_the_providers_own_words(int status)
    {
        var answer = Answered(EndpointCallResult.Refused("busy right now", "text/plain", status, retryAfter: null));

        answer.Succeeded.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeTrue();
        answer.Reason.Should().Be($"{status}: busy right now");
        answer.Outcome[StepAnswer.CallField].Should().Be(Call);
        answer.Outcome[StepAnswer.ReasonField].Should().Be(answer.Reason);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public void Any_other_provider_refusal_is_final(int status)
    {
        var answer = Answered(EndpointCallResult.Refused("no such station", "text/plain", status, retryAfter: null));

        answer.Succeeded.Should().BeFalse();
        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Reason.Should().Be($"{status}: no such station");
    }

    [Fact]
    public void A_provider_answering_in_the_words_of_one_that_is_away_may_be_tried_again()
    {
        var answer = Answered(EndpointCallResult.Refused("404 page not found", "text/plain", 404, retryAfter: null, providerAway: true));

        answer.CanBeTriedAgain.Should().BeTrue();
    }

    [Fact]
    public void A_provider_that_could_not_be_reached_may_be_tried_again()
    {
        var answer = Answered(EndpointCallResult.Failure(new ProblemError(502, "Endpoint call failed", "Connection refused")));

        answer.CanBeTriedAgain.Should().BeTrue();
        answer.Reason.Should().Be("502: Connection refused");
    }

    [Fact]
    public void A_registration_whose_settings_will_not_do_is_final()
    {
        var answer = Answered(Refusal.BadRequest("Request must include a non-empty endpointName.").ToResult());

        answer.CanBeTriedAgain.Should().BeFalse();
        answer.Reason.Should().Be("400: Request must include a non-empty endpointName.");
    }

    [Fact]
    public void The_wait_a_provider_asks_for_in_seconds_is_carried()
    {
        var answer = Answered(EndpointCallResult.Refused("slow down", "text/plain", 429, new RetryConditionHeaderValue(TimeSpan.FromSeconds(30))));

        answer.Wait.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(-10, 0)]
    public void The_wait_a_provider_asks_for_as_a_time_is_measured_from_now(int secondsFromNow, int expectedSeconds)
    {
        var answer = Answered(EndpointCallResult.Refused("slow down", "text/plain", 429, new RetryConditionHeaderValue(Now.AddSeconds(secondsFromNow))));

        answer.Wait.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void A_refusal_that_asks_for_no_wait_carries_none()
    {
        Answered(EndpointCallResult.Refused("down", "text/plain", 503, retryAfter: null)).Wait.Should().BeNull();
    }

    [Fact]
    public void A_long_answer_from_the_provider_is_cut_short_in_the_reason()
    {
        var answer = Answered(EndpointCallResult.Refused(new string('x', 1000), "text/plain", 503, retryAfter: null));

        answer.Reason.Should().Be("503: " + new string('x', StepAnswer.LongestWords) + "…");
    }
}
