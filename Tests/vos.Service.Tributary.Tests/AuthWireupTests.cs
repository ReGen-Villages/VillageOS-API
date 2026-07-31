using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Integration tests for Tributary's auth wireup: when a signing key is configured,
// the authenticated endpoints (/handle, /shutdown) reject anonymous requests
// with 401, but /health stays open.
public class AuthWireupTests
{
    private static TributaryWebApplicationFactory MakeAuthFactory() => new()
    {
        SigningKey = Convert.ToBase64String(new byte[32]),
        Issuer = "VillageOS",
        Audience = "VosClients"
    };

    [Fact]
    public async Task BootWithSigningKey_HealthStillReturns200()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithSigningKey_HandleWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
