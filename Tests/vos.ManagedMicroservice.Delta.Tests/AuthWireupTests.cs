using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Integration tests for Delta's auth wireup: when DELTA_SIGNING_KEY is configured,
/// authenticated endpoints (<c>/handle</c>, <c>/register</c>, <c>/shutdown</c>) reject
/// anonymous requests with 401, but <c>/health</c> remains open. Encodes a real
/// security contract &mdash; a regression here ships an open endpoint.
///
/// Moved from <c>CoverageGapTests.cs</c> under Task #5437 to give the tests an honest
/// home. The <c>AuthEnabledFactory</c> sub-class was replaced with <see cref="EnvVarScope"/>
/// so each test's env vars are restored deterministically regardless of how the factory
/// is disposed.
/// </summary>
[Collection(nameof(DeltaEnvVarCollection))]
public class AuthWireupTests
{
    private static (string SigningKey, string Issuer, string Audience) AuthVars() =>
        (Convert.ToBase64String(new byte[32]), "VillageOS", "VosClients");

    private static EnvVarScope EnableAuth()
    {
        var (key, issuer, audience) = AuthVars();
        return new EnvVarScope(
            ("DELTA_SIGNING_KEY", key),
            ("DELTA_ISSUER", issuer),
            ("DELTA_AUDIENCE", audience));
    }

    [Fact]
    public async Task BootWithSigningKey_HealthStillReturns200()
    {
        using var env = EnableAuth();
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithSigningKey_HandleWithoutBearer_Returns401()
    {
        using var env = EnableAuth();
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_RegisterWithoutBearer_Returns401()
    {
        using var env = EnableAuth();
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        using var env = EnableAuth();
        await using var factory = new DeltaWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.PostAsync("/shutdown", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
