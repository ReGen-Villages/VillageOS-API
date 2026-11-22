using System.Net;
using FluentAssertions;
using vos.Hypha.Registry;
using vos.PlatformClient;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests;

public class TheServerListsWhatAKeyMayUseTests
{
    private const string Models = """[{"Id":"7a1f0f5e-52a4-4c7b-9d0a-3f1f3c1f8a10","Name":"Hillside"}]""";

    private static HttpResponseMessage AnsweringModels(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/api/models" ? Json(Models) : new HttpResponseMessage(HttpStatusCode.NotFound);

    [Fact]
    public async Task A_viewer_is_listed_the_models_tool_and_is_answered_with_the_models()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", AnsweringModels));

        (await server.ListedToolNamesAsync()).Should().BeEquivalentTo(["list_models", "describe_model", "find_things", "get_thing", "read_property"]);

        var answer = await server.Client.CallToolAsync("list_models");
        answer.IsError.Should().NotBe(true);
        TextOf(answer).Should().Contain("Hillside");
    }

    [Fact]
    public async Task The_models_tool_is_marked_as_only_reading()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer"));

        var tool = (await server.Client.ListToolsAsync()).Single(tool => tool.Name == "list_models");

        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().NotBe(true);
    }

    [Fact]
    public async Task A_tool_whose_policy_a_viewer_does_not_satisfy_is_not_listed_and_a_call_to_it_is_refused()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer"), toolTypes: typeof(SampleTools));

        (await server.ListedToolNamesAsync()).Should().NotContain("sample_rename");

        var refused = await server.Client.CallToolAsync(
            "sample_rename", new Dictionary<string, object?> { ["thing"] = Guid.NewGuid(), ["name"] = "Orchard" });

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Be(ToolListing.NotAvailable);
        server.Mycelium.Requests.Should().NotContain(request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task The_same_tool_is_listed_for_an_editor()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor"), toolTypes: typeof(SampleTools));

        (await server.ListedToolNamesAsync()).Should().Contain("sample_rename");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task A_tool_that_needs_a_role_is_listed_only_where_the_model_binds_it(HttpStatusCode boundBy, bool listed)
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "viewer", request =>
                request.RequestUri!.Query == $"?boundBy={SampleTools.BoundRole}"
                    ? Json("""{"Id":"0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44","Name":"Spring"}""", boundBy)
                    : new HttpResponseMessage(HttpStatusCode.NotFound)),
            toolTypes: typeof(SampleTools));

        var names = await server.ListedToolNamesAsync();

        names.Contains("sample_read_where_bound").Should().Be(listed);
    }

    [Fact]
    public async Task A_role_binding_is_asked_of_Mycelium_once_for_one_token()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "viewer", _ => Json("""{"Id":"0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44"}""")),
            toolTypes: typeof(SampleTools));

        await server.ListedToolNamesAsync();
        await server.ListedToolNamesAsync();

        server.Mycelium.Requests
            .Select(request => request.RequestUri!.Query)
            .Where(query => query.StartsWith("?boundBy="))
            .Should().Contain($"?boundBy={SampleTools.BoundRole}")
            .And.OnlyHaveUniqueItems("each role is asked about once, however many tools depend on a role");
    }

    [Fact]
    public async Task The_list_follows_the_role_in_the_token_Mycelium_issues_next()
    {
        var role = "viewer";
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        await using var server = await StartAsync(MyceliumFor(() => role), clock: () => now, toolTypes: typeof(SampleTools));
        (await server.ListedToolNamesAsync()).Should().NotContain("sample_rename");

        role = "editor";
        now = now.AddMinutes(5);

        (await server.ListedToolNamesAsync()).Should().Contain("sample_rename");
    }

    [Fact]
    public async Task A_refusal_from_Mycelium_is_a_failed_tool_call_carrying_its_reason()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", _ => Json("""{"error":"A Thing named Orchard already exists"}""", HttpStatusCode.Conflict)),
            toolTypes: typeof(SampleTools));

        var refused = await server.Client.CallToolAsync(
            "sample_rename", new Dictionary<string, object?> { ["thing"] = Guid.NewGuid(), ["name"] = "Orchard" });

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Contain("409").And.Contain("A Thing named Orchard already exists");
    }

    // Pins what the SDK does with its own exception, which every tool relies on to refuse in words.
    [Fact]
    public async Task A_tool_that_refuses_by_the_sdks_exception_is_a_failed_tool_call_carrying_its_words()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer"), toolTypes: typeof(SampleTools));

        var refused = await server.Client.CallToolAsync("sample_refuses");

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Be("An error occurred invoking 'sample_refuses': That page holds no widget of that name.");
    }

    [Fact]
    public async Task Every_request_to_Mycelium_names_this_program()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", AnsweringModels));

        await server.Client.CallToolAsync("list_models");

        server.Mycelium.Requests.Should().NotBeEmpty().And.OnlyContain(request =>
            request.Headers.GetValues(MyceliumClient.ProgramHeader).Single() == ServerProgram.Name);
    }

    [Fact]
    public async Task A_key_Mycelium_refuses_is_reported_without_showing_the_key()
    {
        await using var server = await StartAsync(_ => Json("""{"error":"Unknown key"}""", HttpStatusCode.Unauthorized));

        var failed = await server.Client.CallToolAsync("list_models");

        failed.IsError.Should().BeTrue();
        TextOf(failed).Should().Contain("Unknown key").And.NotContain(ApiKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.!!!.c")]
    [InlineData("a.e30.c")]
    public void A_token_that_states_no_role_reads_as_no_role(string token) =>
        CallerSource.RoleIn(token).Should().BeEmpty();
}
