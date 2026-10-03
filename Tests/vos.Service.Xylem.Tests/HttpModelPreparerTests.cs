using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Xylem.Services;
using vos.Tests.Shared;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class HttpModelPreparerTests
{
    private const string MyceliumUrl = "http://localhost:5000/";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _mintedToken;
        public HttpRequestMessage? Seen;

        public StubHandler(HttpStatusCode status, string? mintedToken = null)
        {
            _status = status;
            _mintedToken = mintedToken;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (_mintedToken is not null && req.RequestUri!.AbsolutePath.EndsWith("/api/auth/token"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"token\":\"{_mintedToken}\"}}", Encoding.UTF8, "application/json")
                });

            Seen = req;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }

    private sealed class ThrowingHandler(Exception thrown) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(thrown);
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    // Launch settings hand a service its broker address already trimmed; the preparer trims again only
    // because it builds a path onto it.
    private static ServiceCredential Credential(
        IHttpClientFactory factory, string? serviceToken = null, string? apiKey = null) =>
        new(factory, NullLogger.Instance, MyceliumUrl.TrimEnd('/'), serviceToken, apiKey);

    [Fact]
    public async Task Clear_deletes_the_model_with_a_bearer_and_returns_null_on_success()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var factory = new StubFactory(handler);
        var preparer = new HttpModelPreparer(factory, MyceliumUrl, Credential(factory, serviceToken: "tok123"));

        var error = await preparer.ClearModelAsync(default);

        error.Should().BeNull();
        handler.Seen!.Method.Should().Be(HttpMethod.Delete);
        handler.Seen.RequestUri!.ToString().Should().Be("http://localhost:5000/api/model");
        handler.Seen.Headers.Authorization!.Parameter.Should().Be("tok123");
    }

    // Xylem reaches the broker without the shared client, so a service given a key rather than a token
    // would otherwise clear the model with no credential at all and be refused — or worse, not be.
    [Fact]
    public async Task Clear_presents_the_token_a_held_api_key_is_exchanged_for()
    {
        var minted = TestTokens.For(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        var handler = new StubHandler(HttpStatusCode.OK, mintedToken: minted);
        var factory = new StubFactory(handler);
        var preparer = new HttpModelPreparer(factory, MyceliumUrl, Credential(factory, apiKey: "key-1"));

        var error = await preparer.ClearModelAsync(default);

        error.Should().BeNull();
        handler.Seen!.Headers.Authorization!.Parameter.Should().Be(minted);
    }

    [Fact]
    public async Task Clear_returns_an_error_on_non_success()
    {
        var factory = new StubFactory(new StubHandler(HttpStatusCode.InternalServerError));
        var preparer = new HttpModelPreparer(factory, "http://x", Credential(factory));

        var error = await preparer.ClearModelAsync(default);

        error.Should().NotBeNull();
        error.Should().Contain("500");
    }

    [Fact]
    public async Task Clear_returns_an_error_naming_the_broker_when_it_cannot_be_reached()
    {
        var factory = new StubFactory(new ThrowingHandler(new HttpRequestException("Connection refused (localhost:5000)")));
        var preparer = new HttpModelPreparer(factory, MyceliumUrl, Credential(factory));

        var error = await preparer.ClearModelAsync(default);

        error.Should().Be(
            "Failed to clear the model for a new-model ingest: the broker at http://localhost:5000 could not be "
            + "reached (Connection refused (localhost:5000)).");
    }

    [Fact]
    public async Task Clear_returns_an_error_when_the_broker_does_not_answer_in_time()
    {
        var factory = new StubFactory(new ThrowingHandler(new TaskCanceledException("timed out", new TimeoutException())));
        var preparer = new HttpModelPreparer(factory, MyceliumUrl, Credential(factory));

        var error = await preparer.ClearModelAsync(default);

        error.Should().Be(
            "Failed to clear the model for a new-model ingest: the broker at http://localhost:5000 did not answer in time.");
    }

    [Fact]
    public async Task Clear_stopped_by_its_caller_is_still_a_cancellation()
    {
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var factory = new StubFactory(new ThrowingHandler(new TaskCanceledException("cancelled", null, stopped.Token)));
        var preparer = new HttpModelPreparer(factory, MyceliumUrl, Credential(factory));

        var clearing = () => preparer.ClearModelAsync(stopped.Token);

        await clearing.Should().ThrowAsync<OperationCanceledException>();
    }
}
