using System.Net;
using vos.Service.Xylem.Services;
using Xunit;
using FluentAssertions;

namespace vos.Service.Xylem.Tests;

public class HttpModelPreparerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? Seen;
        public StubHandler(HttpStatusCode status) => _status = status;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Seen = req;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    [Fact]
    public async Task Clear_deletes_the_model_with_a_bearer_and_returns_null_on_success()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var preparer = new HttpModelPreparer(new StubFactory(handler), "http://localhost:5000/", "tok123");

        var error = await preparer.ClearModelAsync(default);

        error.Should().BeNull();
        handler.Seen!.Method.Should().Be(HttpMethod.Delete);
        handler.Seen.RequestUri!.ToString().Should().Be("http://localhost:5000/api/model");
        handler.Seen.Headers.Authorization!.Parameter.Should().Be("tok123");
    }

    [Fact]
    public async Task Clear_returns_an_error_on_non_success()
    {
        var preparer = new HttpModelPreparer(new StubFactory(new StubHandler(HttpStatusCode.InternalServerError)), "http://x", null);

        var error = await preparer.ClearModelAsync(default);

        error.Should().NotBeNull();
        error.Should().Contain("500");
    }
}
