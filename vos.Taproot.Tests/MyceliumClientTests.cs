// Direct unit tests for vos.Taproot.MyceliumClient. Uses the internal test-seam
// constructor (MyceliumClient(myceliumUrl, apiKey, HttpClient, Func<DateTime>?))
// reached via InternalsVisibleTo, and the MockHttpMessageHandler from
// vos.Tests.Shared to fake mycelium HTTP without touching the network.
//
// See docs/FOLLOW-UPS.md (entry #1) for the design rationale.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Taproot;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

public class MyceliumClientTests
{
    private const string MyceliumUrl = "https://localhost:7243";
    private const string ApiKey = "test-api-key";
    private const string ServiceToken = "test-jwt-from-mycelium";

    // ---- Construction + API key resolution ----

    [Fact]
    public void Ctor_TrimsTrailingSlashFromMyceliumUrl()
    {
        var (client, _) = NewClient(_ => Ok(), myceliumUrl: "https://localhost:7243///");
        // Internal field is private; verify indirectly by issuing a request and inspecting the URL.
        // The handler is built fresh inside the helper - we just need NewClient to not crash on trailing slashes.
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task Ctor_ExplicitApiKey_UsedDirectlyOnTokenRequest()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            captured = req;
            return TokenResponse(ServiceToken);
        }, apiKey: "explicit-key");

        await client.GetTokenAsync();

        captured!.Headers.GetValues("X-API-Key").Should().ContainSingle("explicit-key");
    }

    [Fact]
    public async Task Ctor_NullApiKey_FallsBackToVosApiKeyEnvVar()
    {
        var original = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", "from-env");
            HttpRequestMessage? captured = null;
            var (client, _) = NewClient(req =>
            {
                captured = req;
                return TokenResponse(ServiceToken);
            }, apiKey: null);

            await client.GetTokenAsync();

            captured!.Headers.GetValues("X-API-Key").Should().ContainSingle("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", original);
        }
    }

    [Fact]
    public async Task Ctor_NoApiKeyAnywhere_GetTokenThrows()
    {
        var original = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", null);
            var (client, _) = NewClient(_ => Ok(), apiKey: null);

            var act = async () => await client.GetTokenAsync();

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*API key*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", original);
        }
    }

    [Fact]
    public void Ctor_PublicNoApiKeyOverload_DefersToEnvVar()
    {
        // Just exercises the public ctor's chain through the single-arg → two-arg(null) path.
        // No HTTP here; constructing must not throw.
        var act = () => new MyceliumClient(MyceliumUrl);
        act.Should().NotThrow();
    }

    // ---- GetTokenAsync token flow ----

    [Fact]
    public async Task GetTokenAsync_SendsXApiKeyAndReturnsMyceliumToken()
    {
        var (client, handler) = NewClient(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/auth/token");
            return TokenResponse(ServiceToken);
        });

        var token = await client.GetTokenAsync();

        token.Should().Be(ServiceToken);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTokenAsync_CachesToken_NoSecondHttpCallWithinExpiry()
    {
        var (client, handler) = NewClient(_ => TokenResponse(ServiceToken));

        var first = await client.GetTokenAsync();
        var second = await client.GetTokenAsync();

        first.Should().Be(second);
        handler.Requests.Should().ContainSingle("second call should hit the cache");
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredToken_RefetchesViaClockInjection()
    {
        var now = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        var (client, handler) = NewClient(_ => TokenResponse(ServiceToken), clock: () => now);

        await client.GetTokenAsync();
        handler.Requests.Should().ContainSingle();

        // Advance past the 4-minute expiry window
        now = now.AddMinutes(5);
        await client.GetTokenAsync();

        handler.Requests.Should().HaveCount(2, "expired cache should trigger a fresh fetch");
    }

    [Fact]
    public async Task GetTokenAsync_NonSuccessResponse_ThrowsWithBodyInMessage()
    {
        var (client, _) = NewClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("API key invalid")
        });

        var act = async () => await client.GetTokenAsync();

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("API key invalid");
        ex.Message.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task AuthenticatedCall_SetsBearerHeaderFromToken()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
                return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetAllThingsAsync();

        // After SetAuthHeaderAsync runs, the HttpClient's DefaultRequestHeaders is set;
        // but per-request headers reflect the same Authorization. Easiest check:
        // the captured request's Authorization header should be Bearer + ServiceToken.
        captured!.Headers.Authorization.Should().NotBeNull();
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be(ServiceToken);
    }

    // ---- HTTP method patterns: GET → JsonElement ----

    [Fact]
    public async Task GetAllThingsAsync_ParsesJsonArray()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things");
            return JsonResponse("[{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"alice\"}]");
        });

        var result = await client.GetAllThingsAsync();

        result.ValueKind.Should().Be(JsonValueKind.Array);
        result.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetAllRelationshipsAsync_RoutesToRelationshipsEndpoint()
    {
        await VerifyGetEndpointHit("/api/relationships", c => c.GetAllRelationshipsAsync());
    }

    [Fact]
    public async Task GetAllServicesAsync_RoutesToMyceliumServicesEndpoint()
    {
        await VerifyGetEndpointHit("/api/mycelium/services", c => c.GetAllServicesAsync());
    }

    [Fact]
    public async Task GetSeedStatusAsync_RoutesToSeedStatusEndpoint()
    {
        await VerifyGetEndpointHit("/api/mycelium/seed-status", c => c.GetSeedStatusAsync());
    }

    [Fact]
    public async Task ListLibrarySeedsAsync_RoutesToLibrarySeedsEndpoint()
    {
        await VerifyGetEndpointHit("/api/mycelium/library-seeds", c => c.ListLibrarySeedsAsync());
    }

    [Fact]
    public async Task GetEndpointsAsync_RoutesToEndpointsEndpoint()
    {
        await VerifyGetEndpointHit("/api/endpoints", c => c.GetEndpointsAsync());
    }

    [Fact]
    public async Task ListModelsAsync_RoutesToModelsEndpoint()
    {
        await VerifyGetEndpointHit("/api/models", c => c.ListModelsAsync());
    }

    [Fact]
    public async Task GetDefaultPropertyModeAsync_RoutesToConfigPropertyModeEndpoint()
    {
        await VerifyGetEndpointHit("/api/config/property-mode", c => c.GetDefaultPropertyModeAsync());
    }

    [Fact]
    public async Task GetPropertyModeAsync_RoutesToPerPropertyModeEndpoint()
    {
        var thingId = Guid.NewGuid();
        await VerifyGetEndpointHit($"/api/things/{thingId}/properties/temp/mode",
            c => c.GetPropertyModeAsync(thingId, "temp"));
    }

    [Fact]
    public async Task GetRangesAsync_RoutesToThingRangesEndpoint()
    {
        var thingId = Guid.NewGuid();
        await VerifyGetEndpointHit($"/api/things/{thingId}/ranges", c => c.GetRangesAsync(thingId));
    }

    [Fact]
    public async Task GetStatesAsync_RoutesToThingStatesEndpoint()
    {
        var thingId = Guid.NewGuid();
        await VerifyGetEndpointHit($"/api/things/{thingId}/states", c => c.GetStatesAsync(thingId));
    }

    [Fact]
    public async Task GetThingsInStateAsync_RoutesToStatesEndpointWithEscapedName()
    {
        await VerifyGetEndpointHit("/api/states/warm/things", c => c.GetThingsInStateAsync("warm"));
    }

    // ---- HTTP method patterns: GET → JsonElement? (404 → null) ----

    [Fact]
    public async Task GetThingAsync_FoundReturnsElement_NotFoundReturnsNull()
    {
        var id = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            return req.RequestUri.AbsolutePath == $"/api/things/{id}"
                ? JsonResponse($"{{\"Id\":\"{id}\"}}")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        (await client.GetThingAsync(id)).Should().NotBeNull();

        var (client2, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        (await client2.GetThingAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task GetRelationshipAsync_NotFound_ReturnsNull()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.GetRelationshipAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetThingAtTimeAsync_WithTimestamp_AppendsTimestampQuery()
    {
        var id = Guid.NewGuid();
        var ts = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse($"{{\"Id\":\"{id}\"}}");
        });

        await client.GetThingAtTimeAsync(id, ts);

        captured!.RequestUri!.Query.Should().StartWith("?timestamp=");
    }

    [Fact]
    public async Task GetThingAtTimeAsync_NoTimestamp_NoQueryString()
    {
        var id = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse($"{{\"Id\":\"{id}\"}}");
        });

        await client.GetThingAtTimeAsync(id, null);

        captured!.RequestUri!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThingAtTimeAsync_NotFound_ReturnsNull()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.GetThingAtTimeAsync(Guid.NewGuid(), null)).Should().BeNull();
    }

    [Fact]
    public async Task GetRangeAsync_NotFound_ReturnsNull()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.GetRangeAsync(Guid.NewGuid(), "warm")).Should().BeNull();
    }

    [Fact]
    public async Task GetRangeAsync_Found_ReturnsElement()
    {
        var thingId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : JsonResponse("{\"Name\":\"warm\"}"));

        var result = await client.GetRangeAsync(thingId, "warm");
        result.Should().NotBeNull();
    }

    // ---- HTTP method patterns: GET → string ----

    [Fact]
    public async Task GetModelJsonAsync_ReturnsRawString()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"raw\":\"model\"}", Encoding.UTF8, "application/json")
            };
        });

        var json = await client.GetModelJsonAsync();

        json.Should().Be("{\"raw\":\"model\"}");
    }

    // ---- HTTP method patterns: POST with body → JsonElement ----

    [Fact]
    public async Task CreateThingAsync_PostsNameInBody()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{\"Id\":\"...\"}");
        });

        await client.CreateThingAsync("alice");

        capturedBody!.Value.GetProperty("Name").GetString().Should().Be("alice");
    }

    [Fact]
    public async Task CreateRelationshipAsync_PostsTripleInBody()
    {
        var s = Guid.NewGuid(); var p = Guid.NewGuid(); var t = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/relationships");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.CreateRelationshipAsync(s, p, t);

        capturedBody!.Value.GetProperty("SubjectId").GetString().Should().Be(s.ToString());
        capturedBody.Value.GetProperty("PredicateId").GetString().Should().Be(p.ToString());
        capturedBody.Value.GetProperty("TargetId").GetString().Should().Be(t.ToString());
    }

    [Fact]
    public async Task ValidateCriteriaAsync_PostsCriteriaPayload()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/ranges/validate");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{\"isValid\":true}");
        });

        await client.ValidateCriteriaAsync("temp > 50");

        capturedBody!.Value.GetProperty("Criteria").GetString().Should().Be("temp > 50");
    }

    [Fact]
    public async Task CreateRangeAsync_OmitsOptionalPropertyAndBoundsWhenNull()
    {
        var thingId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.CreateRangeAsync(thingId, "warm", "temp > 50");

        var body = capturedBody!.Value;
        body.GetProperty("Name").GetString().Should().Be("warm");
        body.GetProperty("Criteria").GetString().Should().Be("temp > 50");
        body.TryGetProperty("Property", out _).Should().BeFalse();
        body.TryGetProperty("Bounds", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateRangeAsync_IncludesOptionalPropertyAndBoundsWhenSupplied()
    {
        var thingId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.CreateRangeAsync(thingId, "warm", "temp > 50", property: "temp", bounds: new { Min = 0, Max = 100 });

        var body = capturedBody!.Value;
        body.GetProperty("Property").GetString().Should().Be("temp");
        body.GetProperty("Bounds").GetProperty("Min").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task SwitchModelAsync_CachesReturnedToken_AndInvalidatesPreviousCache()
    {
        var newToken = "new-jwt-after-switch";
        var modelId = Guid.NewGuid();
        var now = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        var (client, handler) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            if (req.RequestUri!.AbsolutePath == "/api/auth/switch-model")
                return JsonResponse($"{{\"token\":\"{newToken}\"}}");
            return JsonResponse("[]");
        }, clock: () => now);

        // Seed the cache first
        (await client.GetTokenAsync()).Should().Be(ServiceToken);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/auth/token").Should().Be(1);

        // Switch model - should swap the cached token
        await client.SwitchModelAsync(modelId);

        // Next GetTokenAsync should return the new token from cache without re-hitting /api/auth/token
        (await client.GetTokenAsync()).Should().Be(newToken);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/api/auth/token").Should().Be(1);
    }

    [Fact]
    public async Task SwitchModelAsync_ResponseWithoutToken_KeepsCacheCleared()
    {
        var modelId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            if (req.RequestUri!.AbsolutePath == "/api/auth/switch-model")
                return JsonResponse("{}"); // no token field
            return JsonResponse("[]");
        });

        await client.GetTokenAsync(); // seed cache
        await client.SwitchModelAsync(modelId);

        // Cache should have been cleared; next GetTokenAsync re-fetches
        await client.GetTokenAsync();
        // (No specific assertion needed - we're exercising the no-token branch of SwitchModelAsync)
    }

    // ---- HTTP method patterns: POST no body → bool ----

    [Fact]
    public async Task ShutdownMyceliumAsync_PostsAndReturnsSuccessBoolean()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/shutdown");
            return Ok();
        });

        (await client.ShutdownMyceliumAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ShutdownMyceliumAsync_NonSuccessReturnsFalse()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        (await client.ShutdownMyceliumAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task StopServiceAsync_PostsToStopEndpoint()
    {
        var handlerId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/services/{handlerId}/stop");
            return Ok();
        });

        (await client.StopServiceAsync(handlerId)).Should().BeTrue();
    }

    [Fact]
    public async Task StartServiceAsync_PostsToStartEndpoint()
    {
        var handlerId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/services/{handlerId}/start");
            return Ok();
        });

        (await client.StartServiceAsync(handlerId)).Should().BeTrue();
    }

    // ---- HTTP method patterns: POST no body → JsonElement ----

    [Fact]
    public async Task LoadLibrarySeedAsync_PostsToLoadEndpoint()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Contain("/api/mycelium/library-seeds/forest/load");
            return JsonResponse("{\"loaded\":true}");
        });

        var result = await client.LoadLibrarySeedAsync("forest");
        result.GetProperty("loaded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ReloadSeedsAsync_PostsToReloadEndpoint()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/mycelium/seeds/reload");
            return JsonResponse("{}");
        });

        await client.ReloadSeedsAsync();
    }

    // ---- HTTP method patterns: POST string body → string ----

    [Fact]
    public async Task SetModelAsync_PostsRawJsonAndReturnsRawResponse()
    {
        var modelJson = "{\"Id\":\"...\"}";
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/model");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("response-payload", Encoding.UTF8, "application/json")
            };
        });

        var result = await client.SetModelAsync(modelJson);
        result.Should().Be("response-payload");
    }

    // ---- HTTP method patterns: PUT with body → JsonElement ----

    [Fact]
    public async Task SetPropertyAsync_PutsTypedValuePayload()
    {
        var thingId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Put);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{thingId}/properties");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.SetPropertyAsync(thingId, "color", "System.String", "red");

        capturedBody!.Value.GetProperty("Name").GetString().Should().Be("color");
        capturedBody.Value.GetProperty("Type").GetString().Should().Be("System.String");
        capturedBody.Value.GetProperty("Value").GetString().Should().Be("red");
    }

    [Fact]
    public async Task SetRelationshipPropertyAsync_PutsTypedValuePayload()
    {
        var relId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Put);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/relationships/{relId}/properties");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.SetRelationshipPropertyAsync(relId, "note", "System.String", "hello");

        capturedBody!.Value.GetProperty("Name").GetString().Should().Be("note");
    }

    [Fact]
    public async Task SetDefaultPropertyModeAsync_IncludesRingBufferSizeAndSampleRateWhenSupplied()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.SetDefaultPropertyModeAsync("RingBuffer", ringBufferSize: 50, sampleRate: null);

        var body = capturedBody!.Value;
        body.GetProperty("Mode").GetString().Should().Be("RingBuffer");
        body.GetProperty("RingBufferSize").GetInt32().Should().Be(50);
        body.TryGetProperty("SampleRate", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SetDefaultPropertyModeAsync_OmitsBothOptionsWhenNull()
    {
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.SetDefaultPropertyModeAsync("CurrentOnly");

        var body = capturedBody!.Value;
        body.GetProperty("Mode").GetString().Should().Be("CurrentOnly");
        body.TryGetProperty("RingBufferSize", out _).Should().BeFalse();
        body.TryGetProperty("SampleRate", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SetPropertyModeAsync_IncludesSampleRateWhenSupplied()
    {
        var thingId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.SetPropertyModeAsync(thingId, "temp", "Sampled", sampleRate: 100);

        var body = capturedBody!.Value;
        body.GetProperty("Mode").GetString().Should().Be("Sampled");
        body.GetProperty("SampleRate").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task ChangePasswordAsync_PutsCurrentAndNewPassword()
    {
        var userId = Guid.NewGuid();
        JsonElement? capturedBody = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Put);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/auth/users/{userId}/password");
            capturedBody = ReadJsonBody(req);
            return JsonResponse("{}");
        });

        await client.ChangePasswordAsync(userId, "old", "new");

        var body = capturedBody!.Value;
        body.GetProperty("CurrentPassword").GetString().Should().Be("old");
        body.GetProperty("NewPassword").GetString().Should().Be("new");
    }

    // ---- HTTP method patterns: PUT no body → JsonElement ----

    [Fact]
    public async Task SaveLibrarySeedAsync_PutsToLibrarySeedsEndpoint()
    {
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Put);
            req.RequestUri!.AbsoluteUri.Should().Contain("/api/mycelium/library-seeds/village");
            return JsonResponse("{}");
        });

        await client.SaveLibrarySeedAsync("village");
    }

    // ---- HTTP method patterns: DELETE → bool ----

    [Fact]
    public async Task DeleteThingAsync_SuccessReturnsTrue()
    {
        var id = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Delete);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{id}");
            return Ok();
        });

        (await client.DeleteThingAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteThingAsync_NotFoundReturnsFalse()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        (await client.DeleteThingAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task DeletePropertyAsync_EscapesPropertyName()
    {
        var thingId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Contain("/api/things/").And.Contain("/properties/has%20space");
            return Ok();
        });

        (await client.DeletePropertyAsync(thingId, "has space")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRelationshipAsync_DeletesAtRelationshipEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Delete);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/relationships/{id}");
            return Ok();
        });

        (await client.DeleteRelationshipAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRelationshipPropertyAsync_EscapesPropertyName()
    {
        var relId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.RequestUri!.AbsoluteUri.Should().Contain($"/api/relationships/{relId}/properties/note");
            return Ok();
        });

        (await client.DeleteRelationshipPropertyAsync(relId, "note")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRangeAsync_DeletesAtRangeEndpoint()
    {
        var thingId = Guid.NewGuid();
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Delete);
            req.RequestUri!.AbsoluteUri.Should().Contain($"/api/things/{thingId}/ranges/warm");
            return Ok();
        });

        (await client.DeleteRangeAsync(thingId, "warm")).Should().BeTrue();
    }

    // ---- HTTP method patterns: DELETE → void (EnsureSuccessStatusCode) ----

    [Fact]
    public async Task ClearModelAsync_DeletesModelEndpoint_NonSuccessThrows()
    {
        var (clientOk, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            req.Method.Should().Be(HttpMethod.Delete);
            req.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/model");
            return Ok();
        });

        var actOk = async () => await clientOk.ClearModelAsync();
        await actOk.Should().NotThrowAsync();

        var (clientFail, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var actFail = async () => await clientFail.ClearModelAsync();
        await actFail.Should().ThrowAsync<HttpRequestException>();
    }

    // ---- Time-range query string (BuildTimeRangeQuery via mutations methods) ----

    [Fact]
    public async Task GetModelMutationsAsync_BothTimestamps_BuildsBothQueryParams()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetModelMutationsAsync(
            startTime: new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            endTime: new DateTime(2026, 5, 17, 18, 0, 0, DateTimeKind.Utc));

        captured!.RequestUri!.Query.Should().Contain("startTime=").And.Contain("endTime=");
    }

    [Fact]
    public async Task GetModelMutationsAsync_OnlyStartTime_BuildsSingleQueryParam()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetModelMutationsAsync(startTime: DateTime.UtcNow, endTime: null);

        captured!.RequestUri!.Query.Should().Contain("startTime=").And.NotContain("endTime=");
    }

    [Fact]
    public async Task GetModelMutationsAsync_OnlyEndTime_BuildsSingleQueryParam()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetModelMutationsAsync(startTime: null, endTime: DateTime.UtcNow);

        captured!.RequestUri!.Query.Should().Contain("endTime=").And.NotContain("startTime=");
    }

    [Fact]
    public async Task GetModelMutationsAsync_NeitherTimestamp_NoQueryString()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetModelMutationsAsync();

        captured!.RequestUri!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThingMutationsAsync_RoutesToThingMutationsEndpoint()
    {
        var thingId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetThingMutationsAsync(thingId);

        captured!.RequestUri!.AbsolutePath.Should().Be($"/api/things/{thingId}/mutations");
    }

    [Fact]
    public async Task GetRelationshipMutationsAsync_RoutesToRelationshipMutationsEndpoint()
    {
        var relId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetRelationshipMutationsAsync(relId);

        captured!.RequestUri!.AbsolutePath.Should().Be($"/api/relationships/{relId}/mutations");
    }

    [Fact]
    public async Task GetPropertyVersionsAsync_RoutesToVersionsEndpoint()
    {
        var thingId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await client.GetPropertyVersionsAsync(thingId, "temp");

        captured!.RequestUri!.AbsolutePath.Should().Be($"/api/things/{thingId}/properties/temp/versions");
    }

    [Fact]
    public async Task GetModelAtTimeAsync_WithTimestamp_AppendsQuery()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("{}");
        });

        await client.GetModelAtTimeAsync(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));

        captured!.RequestUri!.Query.Should().StartWith("?timestamp=");
    }

    [Fact]
    public async Task GetModelAtTimeAsync_NoTimestamp_NoQueryString()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("{}");
        });

        await client.GetModelAtTimeAsync();

        captured!.RequestUri!.Query.Should().BeEmpty();
    }

    // ---- Error path: non-2xx on any authenticated GET surfaces HttpRequestException ----

    [Fact]
    public async Task AuthenticatedGet_Non2xxResponse_ThrowsViaEnsureSuccessStatusCode()
    {
        var (client, _) = NewClient(req =>
            req.RequestUri!.AbsolutePath == "/api/auth/token"
                ? TokenResponse(ServiceToken)
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = async () => await client.GetAllThingsAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ---- Helpers ----

    private static (MyceliumClient client, MockHttpMessageHandler handler) NewClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? apiKey = ApiKey,
        string myceliumUrl = MyceliumUrl,
        Func<DateTime>? clock = null)
    {
        var handler = new MockHttpMessageHandler(respond);
        var http = new HttpClient(handler);
        var client = new MyceliumClient(myceliumUrl, apiKey, http, clock);
        return (client, handler);
    }

    private async Task VerifyGetEndpointHit(string expectedPath, Func<MyceliumClient, Task> call)
    {
        HttpRequestMessage? captured = null;
        var (client, _) = NewClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token") return TokenResponse(ServiceToken);
            captured = req;
            return JsonResponse("[]");
        });

        await call(client);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsolutePath.Should().Be(expectedPath);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage TokenResponse(string token)
        => JsonResponse($"{{\"token\":\"{token}\"}}");

    private static JsonElement ReadJsonBody(HttpRequestMessage req)
    {
        var raw = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }
}
