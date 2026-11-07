using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Custom WebApplicationFactory for Delta endpoint tests.
// Pattern follows vos.Mycelium.Tests.MyceliumWebApplicationFactory from the sibling
// VillageOS repo, including the IAsyncLifetime workaround for the sync-over-async
// deadlock in CreateHost under the XPlat Code Coverage collector on Windows CI.
// Settings are injected via UseSetting on the host builder; ServiceLaunchSettings.Parse
// falls back to those when no command-line flags are present, which is always the case here.
// Tests that need a per-test verification key set VerificationKey / Issuer /
// Audience on the factory instance before creating a client.
// IHttpClientFactory is replaced with a PerCallHttpClientFactory that returns
// a fresh HttpClient per CreateClient call — MyceliumClientBase.CreateAuthenticatedClientAsync
// mutates client.Timeout on every call, which throws on an already-used HttpClient.
public class DeltaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Per-test routing callback. Set BEFORE the first CreateClient().
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    // Base64 of Mycelium's public signing key, for checking inbound requests. Null = auth disabled.
    public string? VerificationKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    private sealed class PerCallHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        builder.UseSetting("Token", "test-token");
        if (VerificationKey != null) builder.UseSetting("VerificationKey", VerificationKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(HandlerCallback)));
        });
    }
}
