using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests;

/// <summary>
/// Integration tests for Tributary's auth wireup: when TRIBUTARY_SIGNING_KEY is configured,
/// the authenticated endpoints (<c>/handle</c>, <c>/shutdown</c>) reject anonymous requests
/// with 401, but <c>/health</c> stays open.
/// </summary>
[Collection(nameof(TributaryEnvVarCollection))]
public class AuthWireupTests
{
    private static EnvVarScope EnableAuth() => new(
        ("TRIBUTARY_SIGNING_KEY", Convert.ToBase64String(new byte[32])),
        ("TRIBUTARY_ISSUER", "VillageOS"),
        ("TRIBUTARY_AUDIENCE", "VosClients"));

    [Fact]
    public async Task BootWithSigningKey_HealthStillReturns200()
    {
        using var env = EnableAuth();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithSigningKey_HandleWithoutBearer_Returns401()
    {
        using var env = EnableAuth();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        using var env = EnableAuth();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
