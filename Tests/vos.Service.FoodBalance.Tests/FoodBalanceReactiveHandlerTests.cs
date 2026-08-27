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

    // 8.16 ha at 2.5 people per hectare feeds 20.4 of the 320 residents — 6.375%.
    private const string WillowBend = """
        { "productiveFootprintHectares": { "Value": 8.16 }, "peopleFedPerHectarePerYear": { "Value": 2.5 },
          "population": { "Value": 320 } }
        """;

    private static FoodBalanceReactiveHandler NewHandler(RecordingHttpMessageHandler http) =>
        new(new TestHttpClientFactory(new HttpClient(http)), NullLogger<FoodBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    private static RecordingHttpMessageHandler Serving(string properties) =>
        new(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(properties, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task Both_figures_are_computed_and_reported_to_the_caller()
    {
        var http = Serving(WillowBend);

        var outputs = await NewHandler(http).RecomputeAsync(Study);

        outputs.PeopleFed.Should().BeApproximately(20.4, 1e-9);
        outputs.PctOfPopulationFed.Should().BeApproximately(6.375, 1e-9);
    }

    // Both figures are declared as expressions on the shared study archetype, so the model works them out
    // and a derived property refuses every value write. A write left here would throw on the first and
    // abandon the rest, which is why this service now asserts nothing at all.
    [Theory]
    [InlineData("peopleFed")]
    [InlineData("pctOfPopulationFed")]
    public async Task It_asserts_no_figure_the_model_derives_for_itself(string derived)
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post
            && request.Uri == $"http://mycelium/api/things/{Study}/properties/{derived}/facts");
    }

    // The yield is a judgement about growing, not about this place, so it is declared on the shared study
    // archetype and no study owns one. Reading the effective properties is what makes it arrive anyway.
    [Fact]
    public async Task The_read_is_of_the_study_s_effective_properties_so_an_inherited_assumption_arrives()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Get
            && request.Uri == $"http://mycelium{MyceliumRoutes.ThingProperties(Study)}");
    }

    // The same worked example with every figure written as text, which goes through a parse rather than
    // straight off the JSON number. TestCulture explains what a comma-decimal region does to it.
    [Fact]
    public async Task A_string_valued_input_reads_the_same_whatever_the_regional_format()
    {
        var http = Serving("""
            { "productiveFootprintHectares": { "Value": "8.16" }, "peopleFedPerHectarePerYear": { "Value": "2.5" },
              "population": { "Value": "320" } }
            """);

        var outputs = await TestCulture.InAsync(
            TestCulture.CommaDecimal, () => NewHandler(http).RecomputeAsync(Study));

        outputs.PeopleFed.Should().BeApproximately(20.4, 1e-9);
        outputs.PctOfPopulationFed.Should().BeApproximately(6.375, 1e-9);
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
    public async Task A_study_the_broker_will_not_hand_over_is_raised_naming_the_study()
    {
        var http = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => NewHandler(http).RecomputeAsync(Study));

        failure.Message.Should().Contain(Study.ToString()).And.Contain("FoodBalance");
    }

    // Nothing checks that the declared set and the set Compute reads are the same set. A name declared
    // but never read recomputes for a change that cannot move the answer; a name read but never
    // declared leaves the answer stale until something else happens to wake it. The two below say the
    // sets are equal without restating either.
    [Fact]
    public async Task A_study_carrying_exactly_the_declared_inputs_computes()
    {
        var http = Serving(EffectiveProperties.Carrying(FoodBalanceReactiveHandler.InputProperties));

        var outputs = await NewHandler(http).RecomputeAsync(Study);

        outputs.PctOfPopulationFed.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(DeclaredInputs))]
    public async Task Leaving_out_any_declared_input_refuses_the_recompute_by_name(string omitted)
    {
        var http = Serving(EffectiveProperties.Carrying(
            FoodBalanceReactiveHandler.InputProperties.Where(name => name != omitted)));

        var refusal = await Assert.ThrowsAsync<KeyNotFoundException>(() => NewHandler(http).RecomputeAsync(Study));

        refusal.Message.Should().Contain(omitted);
    }

    public static TheoryData<string> DeclaredInputs =>
        new(FoodBalanceReactiveHandler.InputProperties.ToArray());
}
