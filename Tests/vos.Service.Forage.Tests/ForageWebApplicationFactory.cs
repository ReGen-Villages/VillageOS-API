using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Forage.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests;

// Mirrors TributaryWebApplicationFactory: settings arrive through UseSetting because the settings
// reader falls back to those when no command-line flags are present, which is always the case here,
// and IHttpClientFactory is replaced so the subscription read routes through the per-test handler.
public class ForageWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public Func<HttpRequestMessage, HttpResponseMessage> HandlerCallback { get; set; }
        = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    // A run leaves the request that dispatched it, so a test has to be able to wait for the one it
    // started. This keeps every started run and hands them back; the production starter drops them onto
    // the thread pool, where nothing could await one.
    private readonly StartedRuns _runs = new();

    public Task RunsStarted() => _runs.All();

    private sealed class StartedRuns : IDiscoveryRunStarter
    {
        private readonly List<Task> _started = new();

        public void Start(Func<CancellationToken, Task> run)
        {
            lock (_started) _started.Add(run(CancellationToken.None));
        }

        public Task All()
        {
            lock (_started) return Task.WhenAll(_started.ToArray());
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    private sealed class PerCallHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        // disposeHandler=false so the per-test handler outlives each HttpClient.
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost");
        builder.UseSetting("Token", "test-token");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(req => HandlerCallback(req))));

            services.RemoveAll<IDiscoveryRunStarter>();
            services.AddSingleton<IDiscoveryRunStarter>(_runs);
        });
    }
}
