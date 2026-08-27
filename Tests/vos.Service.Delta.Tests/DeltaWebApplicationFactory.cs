using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Delta.Services;
using vos.Service.Delta.Tests.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Custom WebApplicationFactory for Delta endpoint tests.
// Pattern follows vos.Mycelium.Tests.MyceliumWebApplicationFactory from the sibling
// VillageOS repo, including the IAsyncLifetime workaround for the sync-over-async
// deadlock in CreateHost under the XPlat Code Coverage collector on Windows CI
// (VillageOS Bug #5260).
// Settings are injected via UseSetting on the host builder; ServiceLaunchSettings.Parse
// falls back to those when no command-line flags are present, which is always the case here.
// Tests that need a per-test verification key set VerificationKey / Issuer /
// Audience on the factory instance before creating a client.
// The seed is supplied in-memory: ConfigureWebHost swaps the production
// IEndpointSeedProvider (FileEndpointSeedProvider) for an
// InMemoryEndpointSeedProvider seeded from SeedJson, so each
// factory instance owns its seed without touching AppContext.BaseDirectory
// (Task #5455). Tests override SeedJson before the first CreateClient()
// to exercise malformed-seed boot paths.
// IHttpClientFactory is replaced with a PerCallHttpClientFactory that returns
// a fresh HttpClient per CreateClient call — MyceliumClientBase.CreateAuthenticatedClientAsync
// mutates client.Timeout on every call, which throws on an already-used HttpClient.
public class DeltaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Per-test routing callback. Set BEFORE the first CreateClient().
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    // A handler answering synchronously runs each Mycelium call to completion on the caller's thread, so
    // two requests started together still finish one after another and no test can drive them into each
    // other. Set BEFORE the first CreateClient().
    public bool AnswerConcurrently { get; set; }

    // Base64 of Mycelium's public signing key, for checking inbound requests. Null = auth disabled.
    public string? VerificationKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    // In-memory model-seed document passed to InMemoryEndpointSeedProvider at host
    // build (an vos.Service.Delta.Models.EndpointSeedModel: things +
    // relationships). Tests override before the first CreateClient() to drive happy-path,
    // multi-template, malformed, and invalid-graph boot scenarios.
    public string SeedJson { get; set; } = """
    {
      "name": "Endpoint Templates",
      "things": [
        {
          "name": "Endpoint",
          "properties": {
            "url": "https://default.example/",
            "httpMethod": "GET",
            "responseTransform": ""
          }
        }
      ],
      "relationships": []
    }
    """;

    // Provisioning reads the catalog's existing edges before it writes anything. A test that routes
    // nothing for that read is saying nothing about it rather than saying it fails, so it is answered
    // here — after the callback, so a test with something to say about it still has the first word.
    private HttpResponseMessage Route(HttpRequestMessage request)
    {
        var routed = HandlerCallback(request);
        return routed.StatusCode == HttpStatusCode.NotFound
            ? CatalogEdgeRead.Answer(request) ?? routed
            : routed;
    }

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
            Handler = AnswerConcurrently
                ? MockHttpMessageHandler.AnsweringAsynchronously(req => Task.Run(() => Route(req)))
                : new MockHttpMessageHandler(Route);
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));

            services.RemoveAll<IEndpointSeedProvider>();
            services.AddSingleton<IEndpointSeedProvider>(new InMemoryEndpointSeedProvider(SeedJson));
        });
    }
}
