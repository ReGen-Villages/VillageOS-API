using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// While the hazard portal restarts, the proxy in front of it answers `404 page not found` to every
// request for several seconds. Any other 404 is the provider's answer and stays one; the proxy's is told
// apart by its words, asked once more after a wait, and relayed as the refusal it is when it comes again.
public class ProviderUnavailableHandleTests
{
    private const string ProxysWords = "404 page not found\n";
    private const string ApplicationsOwn404 = "<html><head><title>404 Not Found</title></head><body><h1>404 Not Found</h1></body></html>";
    private const string Divisions = """{"data": [{"code": 24889, "admin0": "Portugal", "admin1": "Santarem", "admin2": "Santarem"}]}""";

    private const string SearchEndpoint = """
    {
      "Endpoint.url":        {"Value":"https://portal.test/search?q=Santarem"},
      "Endpoint.httpMethod": {"Value":"GET"}
    }
    """;

    private const string SearchEndpointNamingItsOwnWordsAndWait = """
    {
      "Endpoint.url":                  {"Value":"https://portal.test/search?q=Santarem"},
      "Endpoint.httpMethod":           {"Value":"GET"},
      "Endpoint.unavailableAnswer":    {"Value":"Service Unavailable"},
      "Endpoint.unavailableRetryAfter": {"Value":3}
    }
    """;

    private const string TileEndpoint = """
    {
      "Endpoint.url":          {"Value":"https://portal.test/tile.png"},
      "Endpoint.httpMethod":   {"Value":"GET"},
      "Endpoint.responseKind": {"Value":"binary"}
    }
    """;

    private static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static HttpResponseMessage ProxysAnswer() => Text(HttpStatusCode.NotFound, ProxysWords, "text/plain");

    private sealed class Portal(params HttpResponseMessage[] answers)
    {
        private readonly Queue<HttpResponseMessage> _answers = new(answers);
        public int Asked { get; private set; }

        public HttpResponseMessage Answer()
        {
            Asked++;
            return _answers.Dequeue();
        }
    }

    private sealed record Handled(
        HttpStatusCode Status, string Body, int Asked, IReadOnlyList<TimeSpan> Waits, string ContentRoot);

    private static async Task<Handled> HandleAsync(
        Portal portal, string endpoint = SearchEndpoint, string? configuredWords = "404 page not found",
        string? contentRoot = null)
    {
        var thingId = Guid.NewGuid();
        var clock = new ClockThatWaitsNoTime();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.RequestUri!.Host == "portal.test") return portal.Answer();
            return RouteFindThing(request, thingId, "EP")
                ?? RouteEffectiveProperties(request, thingId, endpoint)
                ?? RouteKindsFromProperties(request, thingId, endpoint)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            if (contentRoot != null) builder.UseContentRoot(contentRoot);
            if (configuredWords != null)
            {
                builder.UseSetting("ProviderUnavailable:Answer", configuredWords);
                builder.UseSetting("ProviderUnavailable:RetryAfterSeconds", "15");
            }
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        });
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP" });
        return new Handled(
            response.StatusCode, await response.Content.ReadAsStringAsync(), portal.Asked, clock.Waits,
            host.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
    }

    // Mycelium starts the service in its own working directory, so the shipped settings have to be found
    // beside the executable rather than in the content root.
    [Fact]
    public async Task The_shipped_settings_are_read_beside_the_executable_whatever_the_content_root()
    {
        var elsewhere = Directory.CreateTempSubdirectory("tributary-content-root-").FullName;

        var handled = await HandleAsync(
            new Portal(ProxysAnswer(), ProxysAnswer()), configuredWords: null, contentRoot: elsewhere);

        handled.ContentRoot.TrimEnd(Path.DirectorySeparatorChar).Should().Be(elsewhere.TrimEnd(Path.DirectorySeparatorChar));
        handled.Status.Should().Be(HttpStatusCode.NotFound);
        handled.Waits.Should().Equal(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task The_proxys_404_answered_twice_is_relayed_as_a_refusal_after_one_wait()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer(), ProxysAnswer()));

        handled.Status.Should().Be(HttpStatusCode.NotFound);
        handled.Body.Should().Be(ProxysWords);
        handled.Asked.Should().Be(2);
        handled.Waits.Should().Equal(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task The_proxys_404_followed_by_the_providers_answer_is_that_answer()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer(), Json(Divisions)));

        handled.Status.Should().Be(HttpStatusCode.OK);
        handled.Body.Should().Be(Divisions);
        handled.Asked.Should().Be(2);
        handled.Waits.Should().ContainSingle();
    }

    // A hazard the portal holds nothing for is answered 404 by the application itself, and that is an
    // answer: asked again every quarter of an hour it would never say anything else.
    [Fact]
    public async Task The_applications_own_404_is_an_answer_asked_once()
    {
        var handled = await HandleAsync(new Portal(Text(HttpStatusCode.NotFound, ApplicationsOwn404, "text/html")));

        handled.Status.Should().Be(HttpStatusCode.OK);
        handled.Body.Should().Be(ApplicationsOwn404);
        handled.Asked.Should().Be(1);
        handled.Waits.Should().BeEmpty();
    }

    [Fact]
    public async Task The_endpoint_Thing_names_its_own_words_and_wait_over_the_configured_ones()
    {
        var itsOwnWords = () => Text(HttpStatusCode.NotFound, "Service Unavailable", "text/plain");

        var handled = await HandleAsync(new Portal(itsOwnWords(), itsOwnWords()), SearchEndpointNamingItsOwnWordsAndWait);

        handled.Status.Should().Be(HttpStatusCode.NotFound);
        handled.Asked.Should().Be(2);
        handled.Waits.Should().Equal(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task The_configured_words_do_not_apply_to_an_endpoint_naming_its_own()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer()), SearchEndpointNamingItsOwnWordsAndWait);

        handled.Status.Should().Be(HttpStatusCode.OK);
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task With_no_words_configured_every_404_is_an_answer()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer()), configuredWords: "");

        handled.Status.Should().Be(HttpStatusCode.OK);
        handled.Body.Should().Be(ProxysWords);
        handled.Asked.Should().Be(1);
    }

    [Fact]
    public async Task A_binary_endpoint_answered_the_proxys_words_twice_is_refused_too()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer(), ProxysAnswer()), TileEndpoint);

        handled.Status.Should().Be(HttpStatusCode.NotFound);
        handled.Asked.Should().Be(2);
        handled.Waits.Should().ContainSingle();
    }

    private const string SearchEndpointNamingTwoWaits = """
    {
      "Endpoint.url":                  {"Value":"https://portal.test/search?q=Santarem"},
      "Endpoint.httpMethod":           {"Value":"GET"},
      "a.unavailableRetryAfter":        {"Value":3},
      "b.unavailableRetryAfter":        {"Value":5}
    }
    """;

    [Fact]
    public async Task Two_registrations_naming_different_waits_are_refused_before_the_provider_is_asked()
    {
        var handled = await HandleAsync(new Portal(ProxysAnswer()), SearchEndpointNamingTwoWaits);

        handled.Status.Should().Be(HttpStatusCode.BadRequest);
        handled.Body.Should().Contain("unavailableRetryAfter");
        handled.Asked.Should().Be(0);
    }
}
