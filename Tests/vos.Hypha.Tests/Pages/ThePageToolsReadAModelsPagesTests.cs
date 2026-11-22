using System.Text.Json;
using FluentAssertions;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests.Pages;

public class ThePageToolsReadAModelsPagesTests
{
    private static readonly string[] s_pageTools = ["list_pages", "read_page", "explain"];

    [Fact]
    public async Task A_viewer_is_listed_the_page_tools_and_each_is_marked_as_only_reading()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer"));

        var tools = (await server.Client.ListToolsAsync()).Where(tool => s_pageTools.Contains(tool.Name)).ToList();

        tools.Select(tool => tool.Name).Should().BeEquivalentTo(s_pageTools);
        tools.Should().OnlyContain(tool => tool.ProtocolTool.Annotations!.ReadOnlyHint == true);
    }

    [Fact]
    public async Task The_pages_a_model_publishes_are_listed_with_the_Things_each_compares_and_one_that_cannot_be_read_is_still_listed()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));

        var pages = Parsed(await server.Client.CallToolAsync("list_pages")).GetProperty("pages").EnumerateArray().ToList();

        var readable = pages.Single(page => page.GetProperty("readable").GetBoolean());
        readable.GetProperty("id").GetString().Should().Be(MyceliumAnsweringFromTheRecording.FirstPage.GetProperty("pageId").GetString());
        readable.GetProperty("compares").EnumerateArray().Select(scope => scope.GetProperty("name").GetString())
            .Should().Equal(MyceliumAnsweringFromTheRecording.FirstPage.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetProperty("name").GetString()));
        var unreadable = pages.Single(page => !page.GetProperty("readable").GetBoolean());
        unreadable.GetProperty("compares").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_page_asked_for_by_name_for_a_Thing_by_name_is_read_as_the_reader_resolved_it()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));
        var page = MyceliumAnsweringFromTheRecording.FirstPage;
        var scope = page.GetProperty("scopes")[0];

        var answer = Parsed(await server.Client.CallToolAsync("read_page", new Dictionary<string, object?>
        {
            ["page"] = page.GetProperty("name").GetString(),
            ["scopes"] = new[] { scope.GetProperty("name").GetString() },
        }));

        var reading = answer.GetProperty("readings").EnumerateArray().Should().ContainSingle().Subject;
        JsonSerializer.Serialize(reading).Should().Be(JsonSerializer.Serialize(page.GetProperty("answers").GetProperty(scope.GetProperty("id").GetString()!)));
        answer.TryGetProperty("notAsked", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_page_read_with_no_Thing_named_is_read_across_everything_it_compares()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));
        var page = MyceliumAnsweringFromTheRecording.FirstPage;

        var answer = Parsed(await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = page.GetProperty("pageId").GetString() }));

        var reading = answer.GetProperty("readings").EnumerateArray().Should().ContainSingle().Subject;
        JsonSerializer.Serialize(reading).Should().Be(JsonSerializer.Serialize(page.GetProperty("answers").GetProperty("")));
    }

    [Fact]
    public async Task A_page_that_is_not_one_of_the_models_is_refused_in_words_naming_it()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = "Spring flows" });

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("Spring flows");
    }

    [Fact]
    public async Task A_page_whose_description_cannot_be_read_is_refused_by_its_name()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));
        var listed = Parsed(await server.Client.CallToolAsync("list_pages")).GetProperty("pages").EnumerateArray();
        var unreadable = listed.Single(page => !page.GetProperty("readable").GetBoolean()).GetProperty("name").GetString();

        var answer = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = unreadable });

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain(unreadable);
    }

    [Fact]
    public async Task A_figure_of_a_Thing_is_explained_as_the_reader_explained_it()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", MyceliumAnsweringFromTheRecording.Answer));
        var thing = MyceliumAnsweringFromTheRecording.FirstThingWithFigures;
        var figure = thing.GetProperty("explained").EnumerateObject().First();

        var answer = Parsed(await server.Client.CallToolAsync("explain", new Dictionary<string, object?>
        {
            ["thing"] = thing.GetProperty("thingId").GetString(),
            ["figure"] = figure.Name,
        }));

        JsonSerializer.Serialize(answer).Should().Be(JsonSerializer.Serialize(figure.Value));
    }

    [Fact]
    public async Task A_page_name_two_pages_hold_is_refused_with_the_identifiers_to_choose_from()
    {
        const string description = """{"title":"Levels","sections":[]}""";
        var selection = HandMadePages.Selection((HandMadePages.FirstPageId, "Levels", description), (HandMadePages.SecondPageId, "Levels", description));
        await using var server = await StartAsync(MyceliumFor(() => "viewer", request =>
            request.RequestUri!.AbsolutePath == "/api/snapshots"
                ? Json($$"""{"watermark":1,"snapshot":{{selection}}}""")
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)));

        var byName = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = "Levels" });
        var byIdentifier = await server.Client.CallToolAsync("read_page", new Dictionary<string, object?> { ["page"] = HandMadePages.SecondPageId });

        byName.IsError.Should().BeTrue();
        TextOf(byName).Should().Contain(HandMadePages.FirstPageId).And.Contain(HandMadePages.SecondPageId);
        byIdentifier.IsError.Should().NotBe(true, TextOf(byIdentifier));
    }

    [Fact]
    public async Task A_figure_asked_of_a_Thing_no_Thing_is_named_as_is_refused_in_words_naming_it()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer"));

        var answer = await server.Client.CallToolAsync("explain", new Dictionary<string, object?> { ["thing"] = "Upper spring", ["figure"] = "flowLitresPerDay" });

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("Upper spring");
    }

    private static JsonElement Parsed(ModelContextProtocol.Protocol.CallToolResult result)
    {
        result.IsError.Should().NotBe(true, TextOf(result));
        return JsonDocument.Parse(TextOf(result)).RootElement;
    }
}
