using System.Net;
using FluentAssertions;
using Xunit;

namespace vos.Service.Shared.Tests;

public class AskingAgainTests
{
    [Fact]
    public void The_pause_doubles_until_it_reaches_five_minutes_and_grows_no_further()
    {
        Enumerable.Range(1, 12).Select(failures => AskingAgain.PauseAfter(failures)).Should().Equal(
            new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 300, 300, 300 }.Select(seconds => TimeSpan.FromSeconds(seconds)));
        AskingAgain.PauseAfter(10_000).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void A_retry_after_the_broker_names_is_the_pause_whatever_the_failures_so_far()
    {
        AskingAgain.PauseAfter(7, TimeSpan.FromSeconds(5)).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_retry_after_past_five_minutes_is_cut_to_five_minutes()
    {
        AskingAgain.PauseAfter(1, TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public void A_success_is_accepted(HttpStatusCode status)
    {
        RegistrationAnswer.To(new HttpResponseMessage(status)).Should().Be(RegistrationAnswer.Accepted);
    }

    [Fact]
    public void A_broker_that_is_still_starting_is_asked_again_after_the_retry_after_it_names()
    {
        var answer = RegistrationAnswer.To(Answering(HttpStatusCode.ServiceUnavailable, retryAfterSeconds: 5));

        answer.Outcome.Should().Be(RegistrationOutcome.AskAgain);
        AskingAgain.PauseAfter(1, answer.RetryAfter).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Too_many_requests_is_asked_again_after_the_retry_after_it_names()
    {
        var answer = RegistrationAnswer.To(Answering(HttpStatusCode.TooManyRequests, retryAfterSeconds: 30));

        answer.Outcome.Should().Be(RegistrationOutcome.AskAgain);
        AskingAgain.PauseAfter(1, answer.RetryAfter).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_request_timeout_is_asked_again_on_the_doubling_pause()
    {
        var answer = RegistrationAnswer.To(new HttpResponseMessage(HttpStatusCode.RequestTimeout));

        answer.Outcome.Should().Be(RegistrationOutcome.AskAgain);
        AskingAgain.PauseAfter(3, answer.RetryAfter).Should().Be(TimeSpan.FromSeconds(4));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void A_server_failure_is_asked_again(HttpStatusCode status)
    {
        RegistrationAnswer.To(new HttpResponseMessage(status)).Outcome.Should().Be(RegistrationOutcome.AskAgain);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public void An_answer_the_broker_would_give_again_is_a_refusal(HttpStatusCode status)
    {
        RegistrationAnswer.To(new HttpResponseMessage(status)).Should().Be(RegistrationAnswer.Refused);
    }

    private static HttpResponseMessage Answering(HttpStatusCode status, int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Add("Retry-After", retryAfterSeconds.ToString());
        return response;
    }
}
