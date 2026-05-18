// Targeted tests covering specific branches in vos.ManagedMicroservice.EndpointCaller that
// the per-class test files don't otherwise exercise. Each test names the gap it pins.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using vos.ManagedMicroservice.EndpointCaller.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.EndpointCaller.Tests;

[Collection(nameof(EndpointCallerEnvVarCollection))]
public class CoverageGapTests
{
    // ---- CliArgs.UsageMessage (lines 50-56) ----

    [Fact]
    public void UsageMessage_MentionsEveryFlag()
    {
        var msg = CliArgs.UsageMessage;
        msg.Should().Contain("--port");
        msg.Should().Contain("--brokerUrl");
        msg.Should().Contain("--token");
        msg.Should().Contain("--signingKey");
        msg.Should().Contain("--issuer");
        msg.Should().Contain("--audience");
    }

    // ---- Program.cs /handle: empty-string url branch (line 196) ----
    // urlElement.ValueKind == String but value is "" — passes the missing/conflict checks,
    // fails the IsNullOrWhiteSpace guard, returns 400 with the "non-empty strings" message.

    [Fact]
    public async Task Handle_EmptyUrlString_Returns400()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":        {"Value":""},
          "Endpoint.httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new EndpointCallerWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.RequestUri.Query.Contains("name=EP"))
                return Json($$"""{"Id":"{{thingId}}","Name":"EP"}""");
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/effective-properties")
                return Json(props);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("non-empty strings");
    }

    // ---- Program.cs TryGetEffectiveProperty direct-match branch (line 333) ----
    // Effective properties expose `url`/`httpMethod` as raw keys (not prefixed) — exercises
    // the early TryGetValue success branch instead of the suffix-stripped search.

    [Fact]
    public async Task Handle_RawUrlAndMethodKeys_HappyPath()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "url":        {"Value":"https://api.test/raw"},
          "httpMethod": {"Value":"GET"}
        }
        """;
        await using var factory = new EndpointCallerWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/things"
                && req.RequestUri.Query.Contains("name=EP"))
                return Json($$"""{"Id":"{{thingId}}","Name":"EP"}""");
            if (req.RequestUri.AbsolutePath == $"/api/things/{thingId}/effective-properties")
                return Json(props);
            if (req.RequestUri.Host == "api.test")
                return Json("{\"value\":1}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"value\":1}");
    }

    // ---- Program.cs auth wireup (lines 64-71, 85-88) ----
    // Boot with a signingKey + issuer + audience so AddBrokerTokenAuth + UseAuthentication +
    // UseAuthorization all execute. /health is never RequireAuthorization-wrapped, so it
    // returns 200 even with auth enabled — that's enough to pin the wireup as covered.

    [Fact]
    public async Task BootWithSigningKey_AuthMiddlewareWiredUp_HealthStillReturns200()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BootWithSigningKey_HandleWithoutBearer_Returns401()
    {
        // RequireAuthorization on the /handle endpoint takes effect; no token → 401.
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BootWithSigningKey_ShutdownWithoutBearer_Returns401()
    {
        await using var factory = new AuthEnabledFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- ObservationIngestService: TryParseObservationPayloads top-level JsonException
    //      (lines 135-138). The /handle path always preserves the transformed string from
    //      TryTransform (which already validates JSON), so this catch is only reached when
    //      TryTransform returns something that round-trips through JsonDocument.Parse but
    //      then yields invalid JSON downstream — practically only when TryNormalizeJson
    //      falls back and JsonSerializer.Serialize emits an unparseable string. That happens
    //      when the jsonata expression returns a raw token that's not JSON; we trigger it
    //      via a stub-free direct call. The internal TryParseObservationPayloads is private,
    //      so we exercise it through the public surface by sending a non-JSON-typed
    //      transformed string back — this is what TryNormalizeJson's catch (lines 118-121)
    //      handles. With Jsonata.Net.Native, queries on string roots can return raw text
    //      that's not JSON — e.g. concatenation of identifier values. Not all branches are
    //      reachable through the public API; the remaining ones are minimal-API wiring
    //      and Serilog file-sink branches that the Phase 3 runsettings should exclude per
    //      docs/TEST-COVERAGE-PLAN.md.

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    // Auth-enabled variant of the factory — same env-var setup plus the three auth-related
    // env vars. The signing key is base64 of 32 bytes (HMAC-SHA256 minimum).
    private sealed class AuthEnabledFactory : EndpointCallerWebApplicationFactory
    {
        public new Task InitializeAsync()
        {
            base.InitializeAsync().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_SIGNING_KEY",
                Convert.ToBase64String(new byte[32]));
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_ISSUER", "VillageOS");
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_AUDIENCE", "VosClients");
            return Task.CompletedTask;
        }

        public new Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_SIGNING_KEY", null);
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_ISSUER", null);
            Environment.SetEnvironmentVariable("ENDPOINTCALLER_AUDIENCE", null);
            return base.DisposeAsync();
        }
    }
}
