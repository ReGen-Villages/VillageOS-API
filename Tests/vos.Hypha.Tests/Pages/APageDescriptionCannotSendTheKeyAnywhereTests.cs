using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests.Pages;

// A page description is the model's data. Whoever may write a page can name any route in a service
// binding, and read_page is a tool marked as only reading, so the server must send none of them.
public class APageDescriptionCannotSendTheKeyAnywhereTests
{
    private const string PageId = HandMadePages.FirstPageId;

    [Theory]
    [InlineData("/api/model/prune")]
    [InlineData("/api/things")]
    [InlineData("/api/accounts")]
    [InlineData("/api/operator-activity")]
    [InlineData("/api/endpoints/../model")]
    [InlineData("https://elsewhere.example/collect")]
    [InlineData("//elsewhere.example/collect")]
    public async Task A_service_binding_naming_any_other_route_is_not_sent_and_the_answer_says_so(string endpoint)
    {
        await using var server = await StartAsync(MyceliumFor(() => "admin", HoldingAPageThatAsks(endpoint)));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = PageId });

        answer.IsError.Should().NotBe(true, TextOf(answer));
        var read = JsonDocument.Parse(TextOf(answer)).RootElement;
        read.GetProperty("notAsked").EnumerateArray().Should().ContainSingle().Which.GetString().Should().Contain(endpoint);
        FigureAskingTheService(read).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        server.Mycelium.Requests.Where(request => request.Method == HttpMethod.Post).Select(request => request.RequestUri!.AbsolutePath)
            .Should().OnlyContain(path => path == "/api/snapshots" || path == "/api/auth/token");
        server.Mycelium.Requests.Should().OnlyContain(request => request.RequestUri!.Host == new Uri(MyceliumUrl).Host);
    }

    // A fetch with the key to the wrong route would read that route's answer into the assistant's hands.
    // The one place a page description reaches a fetched path is a state's name.
    [Theory]
    [InlineData("../../accounts")]
    [InlineData("..")]
    [InlineData("RunningLow/../../model")]
    [InlineData("RunningLow/things/../../../accounts")]
    [InlineData("a/b")]
    [InlineData("a\\..\\accounts")]
    public async Task A_state_named_so_as_to_reach_another_route_is_not_fetched_and_the_answer_says_so(string state)
    {
        var counting = new JsonObject { ["type"] = "kpi", ["title"] = "Counted", ["value"] = new JsonObject { ["kind"] = "stateCount", ["state"] = state } };
        await using var server = await StartAsync(MyceliumFor(() => "admin", HoldingAPageWith(counting)));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = PageId });

        answer.IsError.Should().NotBe(true, TextOf(answer));
        var read = JsonDocument.Parse(TextOf(answer)).RootElement;
        read.GetProperty("notAsked").EnumerateArray().Should().ContainSingle().Which.GetString().Should().Contain("/api/states/");
        Widgets(read).Single(widget => widget.GetProperty("title").GetString() == "Counted").TryGetProperty("unreadable", out _).Should().BeTrue();
        Widgets(read).Single(widget => widget.GetProperty("title").GetString() == "Fixed").GetProperty("value").GetDouble().Should().Be(7);
        server.Mycelium.Requests.Select(request => request.RequestUri!.AbsolutePath)
            .Should().OnlyContain(path => path == "/api/snapshots" || path == "/api/auth/token");
    }

    [Fact]
    public async Task A_state_named_plainly_is_fetched()
    {
        var counting = new JsonObject { ["type"] = "kpi", ["title"] = "Counted", ["value"] = new JsonObject { ["kind"] = "stateCount", ["state"] = "Running low" } };
        await using var server = await StartAsync(MyceliumFor(() => "viewer", HoldingAPageWith(counting)));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = PageId });

        var read = JsonDocument.Parse(TextOf(answer)).RootElement;
        read.TryGetProperty("notAsked", out _).Should().BeFalse();
        Widgets(read).Single(widget => widget.GetProperty("title").GetString() == "Counted").GetProperty("value").GetDouble().Should().Be(3);
    }

    [Fact]
    public async Task A_service_binding_naming_a_service_endpoint_is_sent_there_with_its_body()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", HoldingAPageThatAsks("/api/endpoints/inflow")));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = PageId });

        var read = JsonDocument.Parse(TextOf(answer)).RootElement;
        read.TryGetProperty("notAsked", out _).Should().BeFalse();
        FigureAskingTheService(read).GetProperty("value").GetDouble().Should().Be(42);
        server.Mycelium.Requests.Should().ContainSingle(request => request.RequestUri!.AbsolutePath == "/api/endpoints/inflow");
    }

    [Fact]
    public async Task A_service_Mycelium_refuses_the_key_reads_as_nothing_and_the_rest_of_the_page_still_reads()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", HoldingAPageThatAsks("/api/endpoints/inflow", refusingTheService: true)));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = PageId });

        answer.IsError.Should().NotBe(true, TextOf(answer));
        var read = JsonDocument.Parse(TextOf(answer)).RootElement;
        FigureAskingTheService(read).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        Widgets(read).Single(widget => widget.GetProperty("title").GetString() == "Fixed").GetProperty("value").GetDouble().Should().Be(7);
    }

    private static IEnumerable<JsonElement> Widgets(JsonElement read) =>
        read.GetProperty("readings")[0].GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("widgets").EnumerateArray());

    private static JsonElement FigureAskingTheService(JsonElement read) =>
        Widgets(read).Single(widget => widget.GetProperty("title").GetString() == "Asked of a service");

    private static Func<HttpRequestMessage, HttpResponseMessage> HoldingAPageThatAsks(string endpoint, bool refusingTheService = false) =>
        HoldingAPageWith(
            new JsonObject
            {
                ["type"] = "kpi",
                ["title"] = "Asked of a service",
                ["value"] = new JsonObject { ["kind"] = "service", ["endpoint"] = endpoint, ["select"] = "litres" },
            },
            refusingTheService);

    private static Func<HttpRequestMessage, HttpResponseMessage> HoldingAPageWith(JsonObject widget, bool refusingTheService = false)
    {
        var description = new JsonObject
        {
            ["title"] = "Inflow",
            ["sections"] = new JsonArray(new JsonObject
            {
                ["widgets"] = new JsonArray(
                    new JsonObject { ["type"] = "kpi", ["title"] = "Fixed", ["value"] = new JsonObject { ["kind"] = "const", ["value"] = 7 } },
                    widget),
            }),
        };
        var selection = HandMadePages.Selection((PageId, "Inflow", description.ToJsonString()));

        return request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/snapshots") return Json($$"""{"watermark":1,"snapshot":{{selection}}}""");
            if (path == "/api/states/Running%20low/things") return Json("""{"StateName":"Running low","Count":3}""");
            if (path == "/api/endpoints/inflow")
                return refusingTheService
                    ? Json("""{"error":"This key may not call a service"}""", HttpStatusCode.Forbidden)
                    : Json("""{"litres":42}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }
}
