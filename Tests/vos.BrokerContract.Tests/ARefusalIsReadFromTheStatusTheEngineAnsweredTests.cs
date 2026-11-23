using System.Net;
using vos.BrokerContract.Tests.AgainstTheEngine;
using Xunit;

namespace vos.BrokerContract.Tests;

public class ARefusalIsReadFromTheStatusTheEngineAnsweredTests
{
    private const string AnswerHoldingAnIdentifierWith403 = """{ "Id": "603f8441-7725-403e-babc-ad1a18722110" }""";

    [Fact]
    public async Task An_answer_whose_body_holds_an_identifier_with_403_is_no_refusal()
    {
        var answered = new StatusesTheEngineAnswered(new Answering(HttpStatusCode.OK, AnswerHoldingAnIdentifierWith403));

        await SendThroughAsync(answered);

        Assert.False(answered.RefusedForWantOfAPolicy);
    }

    [Fact]
    public async Task An_answer_of_403_is_a_refusal_whatever_its_body_says()
    {
        var answered = new StatusesTheEngineAnswered(new Answering(HttpStatusCode.Forbidden, ""));

        await SendThroughAsync(answered);

        Assert.True(answered.RefusedForWantOfAPolicy);
    }

    [Fact]
    public async Task What_was_answered_before_it_is_forgotten_does_not_count()
    {
        var answered = new StatusesTheEngineAnswered(new Answering(HttpStatusCode.Forbidden, ""));
        await SendThroughAsync(answered);

        answered.Forget();

        Assert.False(answered.RefusedForWantOfAPolicy);
    }

    private static async Task SendThroughAsync(HttpMessageHandler handler)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://engine.test") };
        using var _ = await client.GetAsync("/api/models");
    }

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
