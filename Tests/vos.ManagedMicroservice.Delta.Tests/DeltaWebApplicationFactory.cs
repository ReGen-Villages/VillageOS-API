using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Custom WebApplicationFactory for Delta endpoint tests.
///
/// Pattern follows <c>vos.Mycelium.Tests.BrokerWebApplicationFactory</c> from the sibling
/// VillageOS repo, including the <c>IAsyncLifetime</c> workaround for the sync-over-async
/// deadlock in <c>CreateHost</c> under the XPlat Code Coverage collector on Windows CI
/// (VillageOS Bug #5260).
///
/// Config is injected via <c>UseSetting</c> on the host builder; <c>CliArgs.Parse</c> reads
/// these as a fallback when CLI args are absent (always the case under WebApplicationFactory).
/// Tests that need a per-test signing key set <see cref="SigningKey"/> / <see cref="Issuer"/> /
/// <see cref="Audience"/> on the factory instance before creating a client.
///
/// A synthetic <c>seed.json</c> is written to <see cref="AppContext.BaseDirectory"/> in
/// <see cref="InitializeAsync"/> so the production <c>EndpointSeedLoader</c> helper finds it
/// on startup (the real <c>seed.json</c> is gitignored per CLAUDE.md as runtime data, so it
/// isn't present in the test process's bin folder by default). The file is removed in
/// <see cref="DisposeAsync"/>. Tests override <see cref="SeedJson"/> before init to exercise
/// malformed-seed boot paths.
///
/// <c>IHttpClientFactory</c> is replaced with a <c>PerCallHttpClientFactory</c> that returns
/// a fresh <c>HttpClient</c> per <c>CreateClient</c> call — <c>BrokerClientBase.CreateAuthenticatedClientAsync</c>
/// mutates <c>client.Timeout</c> on every call, which throws on an already-used <c>HttpClient</c>.
/// </summary>
public class DeltaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Per-test routing callback. Set BEFORE the first <c>CreateClient()</c>.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public MockHttpMessageHandler? Handler { get; private set; }

    /// <summary>Base64 HMAC key for inbound-request JWT validation. Null = auth disabled.</summary>
    public string? SigningKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    /// <summary>Seed contents written to disk in InitializeAsync. Tests can override before init.</summary>
    public string SeedJson { get; set; } = """
    {
      "name": "Endpoint",
      "properties": {
        "url": "https://default.example/",
        "httpMethod": "GET",
        "responseTransform": "$"
      }
    }
    """;

    private string? _writtenSeedPath;

    public Task InitializeAsync()
    {
        _writtenSeedPath = Path.Combine(AppContext.BaseDirectory, "seed.json");
        File.WriteAllText(_writtenSeedPath, SeedJson);
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        if (_writtenSeedPath != null && File.Exists(_writtenSeedPath))
            File.Delete(_writtenSeedPath);

        return base.DisposeAsync().AsTask();
    }

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
        builder.UseSetting("BrokerUrl", "http://localhost");
        builder.UseSetting("Token", "test-token");
        if (SigningKey != null) builder.UseSetting("SigningKey", SigningKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            Handler = new MockHttpMessageHandler(req => HandlerCallback(req));
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(Handler));
        });
    }
}
