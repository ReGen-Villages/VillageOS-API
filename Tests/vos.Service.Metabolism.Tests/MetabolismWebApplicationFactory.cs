using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// Custom WebApplicationFactory for Metabolism endpoint tests.
// Pattern follows vos.Mycelium.Tests.MyceliumWebApplicationFactory from the sibling
// VillageOS repo, including the IAsyncLifetime workaround for the sync-over-async
// deadlock in CreateHost under the XPlat Code Coverage collector on Windows CI
// (VillageOS Bug #5260).
// Settings are injected via UseSetting on the host builder; MetabolismLaunchSettings.Parse
// falls back to those when no command-line flags are present, which is always the case here.
// The Testing environment guard in Program.cs already skips Serilog file logging,
// SSE subscription, and mycelium deregister.
// The test still hits a real MyceliumClient instance inside Program.cs, but no method on it
// is invoked by the /handle / /simulations / /health / /stats / /shutdown endpoints under
// test (they call Metabolism / HandleRequestProcessor, not MyceliumClient). The mycelium URL
// points at http://localhost:1 so any accidental outbound call would fail-fast
// rather than hang.
// One factory per test (each Fact gets a fresh app + state) — Metabolism's request counter
// and simulations dictionary are per-instance, so test isolation requires a fresh app.
public class MetabolismWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Optional per-test service substitutions, applied via ConfigureTestServices
    // after the production registrations land. Set before the first CreateClient().
    // Used by Task #5456's DependencyInjectionTests to swap Metabolism via
    // services.RemoveAll<Metabolism>() + services.AddSingleton(stub).
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
