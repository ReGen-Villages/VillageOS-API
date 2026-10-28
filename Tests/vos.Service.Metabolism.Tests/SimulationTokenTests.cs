using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// The token a service call arrives with expires five minutes after Mycelium issued it, and a simulation
// started by that call ticks for as long as its relationship says.
public class SimulationTokenTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "metabolism-handler";
    private const string SwapRoute = "/api/auth/service-token";

    private static readonly MyceliumSigner Signer = new();
    private static readonly Guid Model = Guid.NewGuid();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string CallToken() => Signer.Token(Issuer, Audience, new Claim(VosClaims.ModelId, Model.ToString()));

    private static object TickingEverySecond(string relationshipId) => new
    {
        relationshipId,
        subjectId = Guid.NewGuid().ToString(),
        targetId = Guid.NewGuid().ToString(),
        properties = new { quantity = 1.5m, unit = "kg", frequencySeconds = 1 },
    };

    private static MetabolismWebApplicationFactory AuthenticatedFactory(
        Func<HttpRequestMessage, HttpResponseMessage> mycelium) =>
        new() { VerificationKey = Signer.VerificationKey, Issuer = Issuer, Audience = Audience, Mycelium = mycelium };

    private static HttpClient ClientCallingWith(MetabolismWebApplicationFactory factory, string callToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", callToken);
        return client;
    }

    [Fact]
    public async Task Bug8006_ASimulationTicksWithATokenSwappedForTheCallsOwn()
    {
        var callToken = CallToken();
        var swappedToken = TestTokens.For(Model, DateTimeOffset.UtcNow.AddHours(24), scope: "endpoint:metabolism:swapped");
        var firstTick = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = AuthenticatedFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath == SwapRoute)
                return Json(HttpStatusCode.OK, $$"""{"token":"{{swappedToken}}"}""");
            if (request.RequestUri.AbsolutePath.StartsWith("/api/things/"))
                firstTick.TrySetResult(request.Headers.Authorization?.Parameter);
            return Json(HttpStatusCode.OK, "{}");
        });
        await factory.InitializeAsync();
        using var client = ClientCallingWith(factory, callToken);

        var response = await client.PostAsJsonAsync("/handle", TickingEverySecond("rel-swapped"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var presentedOnTheFirstTick = await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.DeleteAsync("/simulations/rel-swapped");

        presentedOnTheFirstTick.Should().Be(swappedToken);
    }

    [Fact]
    public async Task Bug8006_ACallWhoseTokenMyceliumWillNotSwap_IsRefusedAndStartsNoSimulation()
    {
        await using var factory = AuthenticatedFactory(request => request.RequestUri!.AbsolutePath == SwapRoute
            ? Json(HttpStatusCode.Forbidden, """{"error":"refused"}""")
            : Json(HttpStatusCode.OK, "{}"));
        await factory.InitializeAsync();
        using var client = ClientCallingWith(factory, CallToken());

        var response = await client.PostAsJsonAsync("/handle", TickingEverySecond("rel-refused"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean().Should().BeFalse();
        (await client.GetFromJsonAsync<JsonElement[]>("/simulations")).Should().BeEmpty();
    }
}
