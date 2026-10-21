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

    // The production starter hands a run to the thread pool, so the run may finish before or after the
    // dispatch is answered. Here no run begins until the test completes it, so a test that reads what a
    // run did without completing it fails every time instead of only on a busy machine.
    private readonly HeldRuns _runs = new();

    public Task CompleteStartedRuns() => _runs.RunAll();

    public IReadOnlyList<Guid> StartedSubjects => _runs.Subjects;

    private sealed class HeldRuns : IDiscoveryRunStarter
    {
        private readonly List<Func<CancellationToken, Task>> _held = new();
        private readonly List<Guid> _subjects = new();

        public IReadOnlyList<Guid> Subjects
        {
            get { lock (_held) return [.. _subjects]; }
        }

        public void Start(Guid subjectId, Func<CancellationToken, Task> run)
        {
            lock (_held)
            {
                _held.Add(run);
                _subjects.Add(subjectId);
            }
        }

        public Task RunAll()
        {
            Func<CancellationToken, Task>[] runs;
            lock (_held)
            {
                runs = _held.ToArray();
                _held.Clear();
            }
            return Task.WhenAll(runs.Select(run => run(CancellationToken.None)));
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
