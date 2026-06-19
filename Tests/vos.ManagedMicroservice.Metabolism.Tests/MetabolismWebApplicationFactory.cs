using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Custom WebApplicationFactory for Metabolism endpoint tests.
///
/// Pattern follows <c>vos.Mycelium.Tests.MyceliumWebApplicationFactory</c> from the sibling
/// VillageOS repo, including the <c>IAsyncLifetime</c> workaround for the sync-over-async
/// deadlock in <c>CreateHost</c> under the XPlat Code Coverage collector on Windows CI
/// (VillageOS Bug #5260).
///
/// Config is injected via <c>UseSetting</c> on the host builder; <c>CliArgs.Parse</c> reads
/// these as a fallback when CLI args are absent (always the case under WebApplicationFactory).
/// The Testing environment guard in <c>Program.cs</c> already skips Serilog file logging,
/// SSE subscription, and mycelium deregister.
///
/// The test still hits a real MyceliumClient instance inside Program.cs, but no method on it
/// is invoked by the /handle / /simulations / /health / /stats / /shutdown endpoints under
/// test (they call Metabolism / HandleRequestProcessor, not MyceliumClient). The mycelium URL
/// points at <c>http://localhost:1</c> so any accidental outbound call would fail-fast
/// rather than hang.
///
/// One factory per test (each Fact gets a fresh app + state) — Metabolism's request counter
/// and simulations dictionary are per-instance, so test isolation requires a fresh app.
/// </summary>
public class MetabolismWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Optional per-test service substitutions, applied via <c>ConfigureTestServices</c>
    /// after the production registrations land. Set before the first <c>CreateClient()</c>.
    /// Used by Task #5456's <c>DependencyInjectionTests</c> to swap <c>Metabolism</c> via
    /// <c>services.RemoveAll&lt;Metabolism&gt;() + services.AddSingleton(stub)</c>.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", "http://localhost:1");
        builder.UseSetting("Mode", "consumes");
        // No SigningKey → auth is disabled (authEnabled=false branch of MapMetabolismEndpoints).

        if (ConfigureServices != null)
            builder.ConfigureTestServices(ConfigureServices);
    }
}
