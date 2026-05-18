using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Custom WebApplicationFactory for Metabolism endpoint tests.
///
/// Pattern follows <c>vos.Mycelium.Tests.BrokerWebApplicationFactory</c> from the sibling
/// VillageOS repo, including the <c>IAsyncLifetime</c> workaround for the sync-over-async
/// deadlock in <c>CreateHost</c> under the XPlat Code Coverage collector on Windows CI
/// (VillageOS Bug #5260).
///
/// Setup:
/// <list type="bullet">
///   <item>Sets <c>ASPNETCORE_ENVIRONMENT=Testing</c> so Program.cs skips Serilog file logging,
///         the SignalR connect callback, and the deregister callback.</item>
///   <item>Sets <c>METABOLISM_PORT</c>, <c>METABOLISM_BROKER_URL</c>, <c>METABOLISM_MODE</c>
///         env vars. CliArgs.Parse reads these as a fallback when CLI args are absent —
///         which they always are under WebApplicationFactory.</item>
/// </list>
///
/// The test still hits a real BrokerClient instance inside Program.cs, but no method on it
/// is invoked by the /handle / /simulations / /health / /stats / /shutdown endpoints under
/// test (they call Metabolism / HandleRequestProcessor, not BrokerClient). The broker URL
/// points at <c>http://localhost:1</c> so any accidental outbound call would fail-fast
/// rather than hang.
///
/// One factory per test (each Fact gets a fresh app + state) — Metabolism's request counter
/// and simulations dictionary are per-instance, so test isolation requires a fresh app.
/// </summary>
public class MetabolismWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        // Env vars must be set before the host is constructed. WebApplicationFactory<Program>
        // constructs lazily on first Server/Client access, so InitializeAsync is the right hook.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("METABOLISM_PORT", "5000");
        Environment.SetEnvironmentVariable("METABOLISM_BROKER_URL", "http://localhost:1");
        Environment.SetEnvironmentVariable("METABOLISM_MODE", "consumes");
        // No --signingKey → auth is disabled for these tests (authEnabled=false branch of MapMetabolismEndpoints).
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("METABOLISM_PORT", null);
        Environment.SetEnvironmentVariable("METABOLISM_BROKER_URL", null);
        Environment.SetEnvironmentVariable("METABOLISM_MODE", null);
        return base.DisposeAsync().AsTask();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The Testing environment guard in Program.cs already skips SignalR + deregister
        // callbacks. No additional service overrides needed for the endpoint-mapper tests.
        builder.UseEnvironment("Testing");
    }
}
