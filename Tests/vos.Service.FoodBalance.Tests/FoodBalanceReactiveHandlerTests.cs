using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.FoodBalance.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.FoodBalance.Tests;

// What the handler does around the arithmetic: which read it makes, what it writes and onto what, and
// which changes it asks to be woken for.
public class FoodBalanceReactiveHandlerTests
{
    private static readonly Guid Study = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed record Recorded(HttpMethod Method, string Uri, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public readonly List<Recorded> Requests = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new Recorded(request.Method, request.RequestUri!.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return responder(request);
        }
    }

    // 8.16 ha at 2.5 people per hectare feeds 20.4 of the 320 residents — 6.375%.
    private const string WillowBend = """
        { "productiveFootprintHectares": { "Value": 8.16 }, "peopleFedPerHectarePerYear": { "Value": 2.5 },
          "population": { "Value": 320 } }
        """;

    private static FoodBalanceReactiveHandler NewHandler(RecordingHandler http) =>
        new(new TestHttpClientFactory(new HttpClient(http)), NullLogger<FoodBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    private static RecordingHandler Serving(string properties) =>
        new(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(properties, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task Both_outputs_are_written_back_onto_the_study_as_facts()
    {
        var http = Serving(WillowBend);

        var outputs = await NewHandler(http).RecomputeAsync(Study);

        outputs.PeopleFed.Should().BeApproximately(20.4, 1e-9);
        http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post
            && request.Uri == $"http://mycelium/api/things/{Study}/properties/{FoodBalanceReactiveHandler.PeopleFedOutput}/facts");
        http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post
            && request.Uri == $"http://mycelium/api/things/{Study}/properties/{FoodBalanceReactiveHandler.PctOfPopulationFedOutput}/facts");
    }

    [Fact]
    public async Task The_value_written_is_the_one_computed_and_not_a_rounded_reading_of_it()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Single(request => request.Uri.Contains(FoodBalanceReactiveHandler.PeopleFedOutput))
            .Body.Should().Contain("20.4");
        http.Requests.Single(request => request.Uri.Contains(FoodBalanceReactiveHandler.PctOfPopulationFedOutput))
            .Body.Should().Contain("6.375");
    }

    // The yield is a judgement about growing, not about this place, so it is declared on the shared study
    // archetype and no study owns one. Reading the resolved properties is what makes it arrive anyway.
    [Fact]
    public async Task The_read_is_of_the_study_s_resolved_properties_so_an_inherited_assumption_arrives()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Get
            && request.Uri == $"http://mycelium{MyceliumRoutes.ThingProperties(Study)}");
    }

    [Fact]
    public async Task An_input_the_study_does_not_carry_is_refused_by_name()
    {
        var http = Serving("""{ "peopleFedPerHectarePerYear": { "Value": 2.5 }, "population": { "Value": 320 } }""");

        var refusal = await Assert.ThrowsAsync<KeyNotFoundException>(() => NewHandler(http).RecomputeAsync(Study));

        refusal.Message.Should().Contain("productiveFootprintHectares").And.Contain("FoodBalance");
        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_refused_write_is_raised_rather_than_reported_as_a_computed_study()
    {
        var http = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(WillowBend, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => NewHandler(http).RecomputeAsync(Study));

        failure.Message.Should().Contain(FoodBalanceReactiveHandler.PeopleFedOutput);
    }

    // The productive footprint is land allocation's output, so a re-run of that service carries through
    // to this balance on its own. Neither of this service's own outputs may be in the set: it writes both
    // onto the study it watches, and watching them would recompute forever.
    [Fact]
    public void The_footprint_wakes_a_recompute_and_this_service_s_own_outputs_do_not()
    {
        FoodBalanceReactiveHandler.InputProperties.Should().BeEquivalentTo(
            ["productiveFootprintHectares", "peopleFedPerHectarePerYear", "population"]);
    }
}
