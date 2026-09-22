using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// Every service presents the same thing to the broker whether it calls through the shared client or
// makes the request itself, so the rule for choosing it lives here rather than at each call.
public class ServiceCredentialTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string StaticToken = "the.static.token";

    private static readonly DateTimeOffset Start = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static ServiceCredential Credential(
        string? serviceToken = null, string? apiKey = null,
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new MockHttpMessageHandler(
            respond ?? (_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        return new ServiceCredential(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl,
            serviceToken, apiKey);
    }

    private static HttpResponseMessage Minted(string token) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"token\":\"{token}\"}}", Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task WithNothingConfigured_ItHoldsNothingToPresent()
    {
        var credential = Credential();

        credential.Holds.Should().BeFalse();
        (await credential.GetTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task WithAStaticToken_ThatIsWhatItPresents()
    {
        var credential = Credential(serviceToken: StaticToken);

        credential.Holds.Should().BeTrue();
        (await credential.GetTokenAsync()).Should().Be(StaticToken);
    }

    [Fact]
    public async Task AKeyAnswersBeforeAStaticToken()
    {
        var minted = TestTokens.For(Guid.NewGuid(), Start.AddHours(1));
        var credential = Credential(serviceToken: StaticToken, apiKey: "key-1", respond: _ => Minted(minted));

        (await credential.GetTokenAsync()).Should().Be(minted);
    }

    [Fact]
    public async Task AKeyThatCouldNotBeExchanged_PresentsNothingRatherThanTheStaticToken()
    {
        var credential = Credential(serviceToken: StaticToken, apiKey: "key-1");

        credential.Holds.Should().BeTrue("a key it cannot exchange is still a credential the operator chose");
        (await credential.GetTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheBearerTheWorkArrivedWith_AnswersBeforeAnythingLaunchedWith()
    {
        var credential = Credential(serviceToken: StaticToken, apiKey: "key-1");

        string? presented = null;
        await MyceliumModelToken.ActingForAsync("inbound-model-token",
            async () => presented = await credential.GetTokenAsync());

        presented.Should().Be("inbound-model-token");
    }

    [Fact]
    public async Task WithNoCredentialOfItsOwn_TheWorkInHandStillCounts()
    {
        var credential = Credential();

        bool? holdsDuring = null;
        await MyceliumModelToken.ActingForAsync("inbound-model-token",
            () => { holdsDuring = credential.Holds; return Task.CompletedTask; });

        holdsDuring.Should().BeTrue();
    }
}
