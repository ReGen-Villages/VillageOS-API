using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// The token a service call arrives with expires five minutes after Mycelium issued it, and a run can take
// far longer.
public class RunTokenTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "forage-handler";
    private const string SwapRoute = "/api/auth/service-token";
    private const string FetchRoute = "/api/endpoints/tributary";

    // Read by the service's clock follower on its own schedule, not by a run.
    private const string ClockRoute = "/api/time";

    private static readonly MyceliumSigner Mycelium = new();
    private static readonly Guid Model = Guid.NewGuid();

    private static Dictionary<string, Guid> Ids() =>
        new[] { "WillowBend", "Earth", "isIn", "covers", "resolvedBy", "OpenMeteo", "OpenMeteoEndpoint" }
            .ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Edge[] Edges() =>
    [
        new Edge("WillowBend", "isIn", "Earth"),
        new Edge("OpenMeteo", "covers", "Earth"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
    ];

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ForageWebApplicationFactory AuthenticatedFactory() =>
        new() { VerificationKey = Mycelium.VerificationKey, Issuer = Issuer, Audience = Audience };

    private static HttpClient ClientCallingWith(ForageWebApplicationFactory factory, string callToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", callToken);
        return client;
    }

    private static string CallToken() => Mycelium.Token(Issuer, Audience, new Claim(VosClaims.ModelId, Model.ToString()));

    [Fact]
    public async Task Bug8006_TheRunWritesWithATokenSwappedForTheCallsOwn()
    {
        var ids = Ids();
        var callToken = CallToken();
        var swappedToken = TestTokens.For(Model, DateTimeOffset.UtcNow.AddHours(24), scope: "endpoint:forage:swapped");
        var presentedByTheRun = new ConcurrentBag<string>();
        string? presentedToTheSwap = null;
        await using var factory = AuthenticatedFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.RequestUri!.AbsolutePath == SwapRoute)
            {
                presentedToTheSwap = request.Headers.Authorization?.Parameter;
                return Json(HttpStatusCode.OK, $$"""{"token":"{{swappedToken}}"}""");
            }
            if (request.RequestUri.AbsolutePath != ClockRoute)
                presentedByTheRun.Add($"{request.RequestUri.AbsolutePath} {request.Headers.Authorization?.Parameter}");
            if (request.RequestUri.AbsolutePath == FetchRoute)
                return Json(HttpStatusCode.OK, """{"success":true}""");
            return RouteSubscription(request, ids, Edges()) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = ClientCallingWith(factory, callToken);

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        presentedToTheSwap.Should().Be(callToken, "a run queued behind another starts after the call's token has expired");

        await factory.CompleteStartedRuns();

        presentedByTheRun.Should().NotBeEmpty().And.OnlyContain(call => call.EndsWith($" {swappedToken}"));
    }

    [Fact]
    public async Task Bug8006_ACallWhoseTokenMyceliumWillNotSwap_IsRefusedAndStartsNoRun()
    {
        var ids = Ids();
        await using var factory = AuthenticatedFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request => request.RequestUri!.AbsolutePath == SwapRoute
            ? Json(HttpStatusCode.Forbidden, """{"error":"refused"}""")
            : RouteSubscription(request, ids, Edges()) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = ClientCallingWith(factory, CallToken());

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<Answer>())!.Success.Should().BeFalse();
        factory.StartedSubjects.Should().BeEmpty();
    }

    private sealed record Answer(bool Success);
}
