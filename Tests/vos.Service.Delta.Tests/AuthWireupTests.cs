using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Delta.Tests;

// Integration tests for Delta's auth wireup: when a signing key is configured,
// authenticated endpoints (/handle, /register, /shutdown) reject
// anonymous requests with 401, but /health remains open. Encodes a real
// security contract — a regression here ships an open endpoint.
public class AuthWireupTests
{
    private static DeltaWebApplicationFactory MakeAuthFactory() => new()
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

        var response = await client.PostAsJsonAsync("/handle", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_RegisterWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.PostAsync("/shutdown", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
