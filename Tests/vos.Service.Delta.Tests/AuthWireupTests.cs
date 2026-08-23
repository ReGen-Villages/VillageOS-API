using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Integration tests for Delta's auth wireup: when a verification key is configured,
// authenticated endpoints (/handle, /register, /shutdown) reject
// anonymous requests with 401, but /health remains open. Encodes a real
// security contract — a regression here ships an open endpoint.
public class AuthWireupTests
{
    private static DeltaWebApplicationFactory MakeAuthFactory() => new()
    {
        VerificationKey = new MyceliumSigner().VerificationKey,
        Issuer = "VillageOS",
        Audience = "delta-handler"
    };

    [Fact]
    public async Task BootWithAVerificationKey_HealthStillReturns200()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithAVerificationKey_HandleWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithAVerificationKey_RegisterWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new { name = "X", properties = new { url = "x" } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithAVerificationKey_ShutdownWithoutBearer_Returns401()
    {
        await using var factory = MakeAuthFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        (await client.PostAsync("/shutdown", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
