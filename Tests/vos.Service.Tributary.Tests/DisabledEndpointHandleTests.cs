using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public class DisabledEndpointHandleTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "vos-disabled-endpoint-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, recursive: true);
    }

    [Fact]
    public async Task A_disabled_endpoint_is_refused_and_its_provider_is_not_called()
    {
        var (response, providerCalls) = await HandleAsync("""
            "Endpoint.enabled": {"Value":false}
            """);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("EP").And.Contain("disabled");
        providerCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_disabled_endpoint_is_not_sent_for_a_token()
    {
        var (response, providerCalls) = await HandleAsync("""
            "Endpoint.enabled":       {"Value":false},
            "Endpoint.tokenUrl":      {"Value":"https://provider.test/token"},
            "Endpoint.tokenRequest":  {"Value":{"username":"alice"}},
            "Endpoint.tokenPath":     {"Value":"token"}
            """, new Kind(EndpointKindRoles.Authentication, "TokenExchangeAuthentication", "tokenUrl", "tokenRequest", "tokenPath"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        providerCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_disabled_endpoint_is_refused_for_being_disabled_before_anything_else_wrong_with_it()
    {
        var (response, _) = await HandleAsync("""
            "Endpoint.enabled": {"Value":false}
            """, new Kind(EndpointKindRoles.Caching, "DiskCache", "cacheTtl"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_body_kept_on_disk_is_not_served_once_the_endpoint_is_disabled()
    {
        var kept = """
            "Endpoint.cacheTtl": {"Value":"300"}
            """;
        var (whileEnabled, firstCalls) = await HandleAsync(kept, new Kind(EndpointKindRoles.Caching, "DiskCache", "cacheTtl"));
        var (onceDisabled, _) = await HandleAsync($$"""
            {{kept}},
            "Endpoint.enabled": {"Value":false}
            """, new Kind(EndpointKindRoles.Caching, "DiskCache", "cacheTtl"));

        whileEnabled.StatusCode.Should().Be(HttpStatusCode.OK);
        firstCalls.Should().ContainSingle();
        onceDisabled.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_enabled_endpoint_is_called()
    {
        var (response, providerCalls) = await HandleAsync("""
            "Endpoint.enabled": {"Value":true}
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        providerCalls.Should().ContainSingle();
    }

    private async Task<(HttpResponseMessage Response, List<Uri> ProviderCalls)> HandleAsync(string properties, params Kind[] kinds)
    {
        var endpointId = Guid.NewGuid();
        var effective = $$"""
            {
              "Endpoint.url":        {"Value":"https://provider.test/data"},
              "Endpoint.httpMethod": {"Value":"GET"},
              {{properties}}
            }
            """;
        var providerCalls = new List<Uri>();
        await using var factory = new TributaryWebApplicationFactory { CacheDirectory = _cacheDirectory };
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.RequestUri!.Host == "provider.test")
            {
                providerCalls.Add(request.RequestUri);
                return Json("""{"token":"minted"}""");
            }
            return RouteFindThing(request, endpointId, "EP")
                ?? RouteEffectiveProperties(request, endpointId, effective)
                ?? RouteKinds(request, endpointId, kinds)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        return (await client.PostAsJsonAsync("/handle", new { endpointName = "EP" }), providerCalls);
    }
}
