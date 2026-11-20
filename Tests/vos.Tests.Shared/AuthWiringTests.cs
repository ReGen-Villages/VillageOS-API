using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace vos.Tests.Shared;

public abstract class AuthWiringTests<TFactory, TProgram>
    where TFactory : ServiceWebApplicationFactory<TProgram>, new()
    where TProgram : class
{
    protected const string Issuer = "VillageOS";

    protected static readonly MyceliumSigner Mycelium = new();

    protected abstract string Audience { get; }

    protected abstract IReadOnlyList<(string Route, object? Body)> GuardedRoutes { get; }

    protected TFactory Keyed() => new() { VerificationKey = Mycelium.VerificationKey, Issuer = Issuer, Audience = Audience };

    [Fact]
    public async Task Health_answers_a_call_with_no_token()
    {
        await using var factory = Keyed();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Each_guarded_route_refuses_a_call_with_no_token()
    {
        await using var factory = Keyed();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        foreach (var (route, body) in GuardedRoutes)
        {
            var response = body is null ? await client.PostAsync(route, null) : await client.PostAsJsonAsync(route, body);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route} answered {(int)response.StatusCode}");
        }
    }
}
