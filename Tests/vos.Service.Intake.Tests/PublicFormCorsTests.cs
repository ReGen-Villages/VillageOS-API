using System.Net;
using FluentAssertions;
using Xunit;

namespace vos.Service.Intake.Tests;

// The public form is served from the main hostname and the intake service answers on its own, so the
// browser asks the service whether that origin may call it. Only the configured origin is allowed;
// with none configured the service answers no cross-origin caller at all.
public class PublicFormCorsTests
{
    private const string FormOrigin = "https://app.example.org";

    private static IntakeWebApplicationFactory Factory(string? publicFormOrigin) => new()
    {
        PublicFormOrigin = publicFormOrigin,
        HandlerCallback = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/submissions");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    [Fact]
    public async Task APreflightFromTheConfiguredOrigin_IsAllowed()
    {
        await using var factory = Factory(FormOrigin);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight(FormOrigin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be(FormOrigin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Should().ContainSingle()
            .Which.Should().Contain("POST");
    }

    [Fact]
    public async Task ASubmissionFromTheConfiguredOrigin_CarriesTheAllowHeader()
    {
        await using var factory = Factory(FormOrigin);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/submissions")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", FormOrigin);

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be(FormOrigin);
    }

    [Fact]
    public async Task AnotherOrigin_GetsNoAllowHeader()
    {
        await using var factory = Factory(FormOrigin);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("https://elsewhere.example.org"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task WithNoConfiguredOrigin_NoCrossOriginCallerIsAllowed()
    {
        await using var factory = Factory(publicFormOrigin: null);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight(FormOrigin));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task ASecondConfiguredOrigin_IsAllowedToo()
    {
        await using var factory = Factory($"{FormOrigin}, https://staging.example.org");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("https://staging.example.org"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle()
            .Which.Should().Be("https://staging.example.org");
    }
}
