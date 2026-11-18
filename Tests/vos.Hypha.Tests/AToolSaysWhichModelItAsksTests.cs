using System.ComponentModel;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using vos.Mcp.Registry;
using vos.Tests.Shared;
using Xunit;
using static vos.Mcp.Tests.ServerUnderTest;

namespace vos.Mcp.Tests;

public class AToolSaysWhichModelItAsksTests
{
    private const string Hillside = "7a1f0f5e-52a4-4c7b-9d0a-3f1f3c1f8a10";
    private const string Riverbend = "2c9d6b1e-8f0a-4d67-b1a3-6e5f0a9c4d22";
    private const string BoundRole = "5f0c4a56-3f6c-5d0b-9a55-0d2f1b6a7c11";

    private static readonly string Models =
        $$"""[{"Id":"{{Hillside}}","Name":"Hillside"},{"Id":"{{Riverbend}}","Name":"Riverbend"}]""";

    private static string TokenFor(string modelQuery) =>
        TestTokens.Jwt(new Dictionary<string, object> { [ClaimTypes.Role] = "viewer", ["asked"] = modelQuery });

    // The key's own model is Hillside, and only Riverbend binds the role.
    private static HttpResponseMessage TwoModels(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var forRiverbend = request.Headers.Authorization?.Parameter == TokenFor($"?modelId={Riverbend}");
        return path switch
        {
            "/api/auth/token" => Json($$"""{"token":"{{TokenFor(request.RequestUri.Query)}}"}"""),
            "/api/models" => Json(Models),
            "/api/things" when request.RequestUri.Query.StartsWith("?boundBy=") =>
                forRiverbend ? Json("""{"Name":"Spring"}""") : new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/things" => Json(forRiverbend ? """[{"Name":"A Thing in Riverbend"}]""" : """[{"Name":"A Thing in Hillside"}]"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static Dictionary<string, object?> Asking(string? model) =>
        model is null ? [] : new() { [ModelClients.Parameter] = model };

    [Theory]
    [InlineData(null, "A Thing in Hillside")]
    [InlineData("Riverbend", "A Thing in Riverbend")]
    [InlineData(Riverbend, "A Thing in Riverbend")]
    [InlineData("Hillside", "A Thing in Hillside")]
    public async Task A_tool_asks_the_model_it_is_given_by_name_or_identifier_and_its_own_when_given_none(
        string? model, string answered)
    {
        await using var server = await StartAsync(TwoModels, toolTypes: typeof(ToolsThatTakeAModel));

        var answer = await server.Client.CallToolAsync("sample_things", Asking(model));

        TextOf(answer).Should().Contain(answered);
    }

    [Fact]
    public async Task A_model_the_key_does_not_reach_is_refused_with_the_models_it_does_reach_and_nothing_is_asked()
    {
        await using var server = await StartAsync(TwoModels, toolTypes: typeof(ToolsThatTakeAModel));

        var refused = await server.Client.CallToolAsync("sample_things", Asking("Lakeside"));

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Contain("'Lakeside'").And.Contain("Hillside").And.Contain("Riverbend");
        server.Mycelium.Requests.Should().NotContain(request =>
            request.RequestUri!.AbsolutePath == "/api/things" && request.RequestUri.Query == "");
    }

    [Fact]
    public async Task A_name_more_than_one_model_holds_is_refused_with_the_identifiers_to_choose_from()
    {
        var twoOfOneName = $$"""[{"Id":"{{Hillside}}","Name":"Orchard"},{"Id":"{{Riverbend}}","Name":"Orchard"}]""";
        await using var server = await StartAsync(
            request => request.RequestUri!.AbsolutePath == "/api/models" ? Json(twoOfOneName) : TwoModels(request),
            toolTypes: typeof(ToolsThatTakeAModel));

        var refused = await server.Client.CallToolAsync("sample_things", Asking("Orchard"));

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Contain(Hillside).And.Contain(Riverbend);
    }

    [Fact]
    public async Task The_key_is_exchanged_once_for_a_model_however_many_calls_name_it()
    {
        await using var server = await StartAsync(TwoModels, toolTypes: typeof(ToolsThatTakeAModel));

        await server.Client.CallToolAsync("sample_things", Asking("Riverbend"));
        await server.Client.CallToolAsync("sample_things", Asking(Riverbend));

        server.Mycelium.Requests.Count(request => request.RequestUri!.PathAndQuery == $"/api/auth/token?modelId={Riverbend}")
            .Should().Be(1);
    }

    [Fact]
    public async Task A_tool_that_needs_a_role_is_refused_for_a_model_that_does_not_bind_it_and_answers_for_one_that_does()
    {
        await using var server = await StartAsync(TwoModels, toolTypes: typeof(ToolsThatTakeAModel));

        var whereBound = await server.Client.CallToolAsync("sample_bound", Asking("Riverbend"));
        var whereNotBound = await server.Client.CallToolAsync("sample_bound", Asking("Hillside"));

        TextOf(whereBound).Should().Contain("Spring");
        whereNotBound.IsError.Should().BeTrue();
        TextOf(whereNotBound).Should().Be(ToolListing.NotAvailable);
    }

    [Fact]
    public async Task The_list_is_decided_by_the_keys_own_model_whatever_model_a_call_names()
    {
        await using var server = await StartAsync(TwoModels, toolTypes: typeof(ToolsThatTakeAModel));

        await server.Client.CallToolAsync("sample_bound", Asking("Riverbend"));

        (await server.ListedToolNamesAsync()).Should().Contain("sample_things").And.NotContain("sample_bound");
    }

    [McpServerToolType]
    public sealed class ToolsThatTakeAModel(ModelClients clients)
    {
        [McpServerTool(Name = "sample_things", ReadOnly = true)]
        [Description("Lists the Things of a model.")]
        [RequiresPolicy(MyceliumPolicy.ReadModel)]
        public async Task<JsonElement> ThingsAsync(string? model = null) =>
            await (await clients.ForAsync(model)).GetAllThingsAsync();

        [McpServerTool(Name = "sample_bound", ReadOnly = true)]
        [Description("Reads the Thing a role binds in a model.")]
        [RequiresPolicy(MyceliumPolicy.ReadModel)]
        [RequiresBoundRole(BoundRole)]
        public async Task<JsonElement?> BoundAsync(string? model = null) =>
            await (await clients.ForAsync(model)).GetThingBoundByAsync(Guid.Parse(BoundRole));
    }
}
