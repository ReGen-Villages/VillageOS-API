using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace vos.Service.CSharp.Echo.Tests;

// The example started with neither ApiKey nor Token: each demo route answers why rather than a bare 500.
public class DemoRouteTests : IClassFixture<DemoRouteTests.WithNoCredential>
{
    private readonly HttpClient _client;

    public DemoRouteTests(WithNoCredential host) => _client = host.CreateClient();

    [Theory]
    [InlineData("/demo/write-kinds", """{"thingId":"33333333-3333-3333-3333-333333333333"}""")]
    [InlineData("/demo/subscribe", "{}")]
    public async Task A_demo_route_answers_that_no_credential_is_set(string route, string body)
    {
        var answer = await _client.PostAsync(route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        answer.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var reason = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
        reason.Should().Contain("neither ApiKey nor Token is set");
    }

    public sealed class WithNoCredential : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Port", "5000");
            builder.UseSetting("MyceliumUrl", "http://localhost:1");
        }
    }
}
