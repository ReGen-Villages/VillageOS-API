using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using vos.Hypha.Registry;
using vos.Hypha.Tools.Pages;
using vos.Pages;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests.Pages;

public class ThePressToolsTests
{
    private const string Springs = "Springs waiting for a decision";
    private const string ColdSpring = "d5abcde5-800f-578f-8cb8-89b66825f468";
    private const string DecidedAs = "ae41e56e-ac72-5396-9c35-657dcb561b1c";
    private const string Capped = "2420729f-9c3e-5e20-a37c-41bfaaea0e39";
    private const string Reason = "The spring runs dry each August";
    private const string HandMadePage = HandMadePages.FirstPageId;
    private const string Waiting = "Waiting";
    private const string NorthPond = "00000000-0000-4000-8000-000000000301";

    private static readonly string s_catchmentsPage = MyceliumAnsweringFromTheRecording.FirstPage.GetProperty("pageId").GetString()!;

    [Fact]
    public async Task An_editor_in_a_model_with_pages_is_listed_both_tools_and_the_press_is_marked_as_destroying()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", ModelTakingPresses().Answer));

        var tools = (await server.Client.ListToolsAsync()).ToDictionary(tool => tool.Name, tool => tool.ProtocolTool.Annotations!);

        (await server.ListedToolNamesAsync()).Should().BeEquivalentTo(["list_models", "list_pages", "read_page", "explain", "offered_choices", "press"]);
        tools["offered_choices"].ReadOnlyHint.Should().BeTrue();
        tools["press"].ReadOnlyHint.Should().BeFalse();
        tools["press"].DestructiveHint.Should().BeTrue();
    }

    [Fact]
    public async Task A_viewer_is_listed_the_choices_and_not_the_press()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", ModelTakingPresses().Answer));

        (await server.ListedToolNamesAsync()).Should().BeEquivalentTo(["list_models", "list_pages", "read_page", "explain", "offered_choices"]);
    }

    [Fact]
    public async Task Neither_tool_is_listed_where_the_model_binds_nothing_to_the_page_role()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", ModelTakingPresses(bindsThePageRole: false).Answer));

        (await server.ListedToolNamesAsync()).Should().BeEquivalentTo(["list_models", "list_pages", "read_page", "explain"]);
    }

    [Fact]
    public async Task The_choices_a_row_is_offered_are_answered_by_their_keys_and_labels()
    {
        await using var server = await StartAsync(MyceliumFor(() => "viewer", ModelTakingPresses().Answer));

        var offered = Parsed(await server.Client.CallToolAsync("offered_choices", new Dictionary<string, object?>
        {
            ["page"] = "Catchments", ["widget"] = Springs, ["row"] = "Cold spring",
        }));

        offered.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("key").GetString())
            .Should().BeEquivalentTo(["listed:0", Capped, "2ab3fd24-8196-5c5e-8007-7883d6bc293f", "listed:1", "listed:2"]);
    }

    [Fact]
    public async Task The_choices_a_row_is_offered_on_a_page_read_for_one_Thing_are_read_on_that_Things_page()
    {
        var scopes = MyceliumAnsweringFromTheRecording.FirstPage.GetProperty("scopes");
        var forOne = scopes[0].GetProperty("id").GetString()!;
        var forAnother = scopes[1].GetProperty("id").GetString()!;
        var selected = new ConcurrentQueue<string>();
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "viewer", request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/snapshots") selected.Enqueue(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return model.Answer(request);
        }));

        var offered = Parsed(await server.Client.CallToolAsync("offered_choices", new Dictionary<string, object?>
        {
            ["page"] = "Catchments", ["widget"] = Springs, ["row"] = "Cold spring", ["scope"] = scopes[0].GetProperty("name").GetString(),
        }));

        offered.GetProperty("choices").GetArrayLength().Should().BePositive();
        selected.Should().Contain(body => body.Contains(forOne) && !body.Contains(forAnother));
    }

    [Fact]
    public async Task A_press_previewed_answers_its_writes_and_a_digest_and_sends_nothing()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var previewed = Parsed(await server.Client.CallToolAsync("press", Pressing(Capped)));

        previewed.GetProperty("sent").GetBoolean().Should().BeFalse();
        previewed.GetProperty("digest").GetString().Should().NotBeNullOrEmpty();
        previewed.GetProperty("composed").GetProperty("writes").EnumerateArray().Select(write => write.GetProperty("write").GetString())
            .Should().Equal("relate", "stamp");
        model.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_press_sent_without_the_digest_by_a_chat_application_that_cannot_ask_the_person_is_shown_and_not_written()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = Parsed(await server.Client.CallToolAsync("press", Pressing(Capped, preview: false)));

        answer.GetProperty("sent").GetBoolean().Should().BeFalse();
        answer.GetProperty("refusal").GetString().Should().Contain(Confirmation.DigestParameter);
        model.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_press_sent_with_the_digest_it_was_shown_relates_the_row_stamps_it_and_reports_each_write_with_the_reason()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await PressedAsync(server, Capped);

        answer.GetProperty("sent").GetBoolean().Should().BeTrue();
        model.Writes.Select(write => write.Route).Should().Equal(
            "POST /api/relationships", $"PUT /api/things/{ColdSpring}/properties");
        JsonDocument.Parse(model.Writes[0].Body).RootElement.Should().BeEquivalentTo(JsonDocument.Parse(
            $$"""{"SubjectId":"{{ColdSpring}}","PredicateId":"{{DecidedAs}}","TargetId":"{{Capped}}"}""").RootElement, options => options.ComparingByMembers<JsonElement>());
        model.Writes[1].Body.Should().Contain("\"decidedAt\"").And.Contain("vos.DateTime");
        model.Reports.Should().HaveCount(2).And.OnlyContain(report => report.Contains(QuotedInAReport(Reason)));
        model.Reports[0].Should().Contain($"relate Thing {ColdSpring} to Thing {Capped}");
        model.Reports[1].Should().Contain($"set property \\u0022decidedAt\\u0022 on Thing {ColdSpring}");
    }

    [Fact]
    public async Task A_digest_of_another_press_writes_nothing_and_shows_this_press_with_its_own_digest()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));
        var leftAsItIs = Parsed(await server.Client.CallToolAsync("press", Pressing("2ab3fd24-8196-5c5e-8007-7883d6bc293f")));

        var answer = Parsed(await server.Client.CallToolAsync("press", Pressing(Capped, preview: false, digest: leftAsItIs.GetProperty("digest").GetString())));

        answer.GetProperty("sent").GetBoolean().Should().BeFalse();
        answer.GetProperty("digest").GetString().Should().NotBe(leftAsItIs.GetProperty("digest").GetString());
        model.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_choice_with_its_own_service_route_is_posted_there_and_the_post_is_reported_with_the_reason()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await PressedAsync(server, "listed:1", new Dictionary<string, object> { ["days"] = 3, ["reservoir"] = "North pond" });

        answer.GetProperty("answer").GetProperty("said").GetString().Should().Be("A flow test is booked");
        model.Writes.Select(write => write.Route).Should().Equal("POST /api/endpoints/echo", $"PUT /api/things/{ColdSpring}/properties");
        JsonDocument.Parse(model.Writes[0].Body).RootElement.GetRawText().Should().Be("""{"view":"flowTest","record":"Cold spring","days":3,"reservoir":"North pond"}""");
        model.Reports[0].Should().Contain("send a form to echo").And.Contain(QuotedInAReport(Reason));
    }

    [Fact]
    public async Task A_press_to_a_platform_route_that_is_not_listed_is_not_sent_the_row_is_not_stamped_and_the_answer_names_the_route()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await server.Client.CallToolAsync("press", Pressing("listed:2", preview: false, digest: await DigestOfAsync(server, "listed:2")));

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("/api/operator-activity");
        model.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_listed_choice_that_relates_the_row_to_no_Thing_is_not_sent()
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await server.Client.CallToolAsync("press", Pressing("listed:0", preview: false, digest: await DigestOfAsync(server, "listed:0")));

        answer.IsError.Should().BeTrue();
        model.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_press_with_no_reason_is_refused_before_anything_is_read(string? reason)
    {
        var model = ModelTakingPresses();
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));
        var arguments = Pressing(Capped);
        arguments["reason"] = reason;

        var answer = await server.Client.CallToolAsync("press", arguments);

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("reason");
        model.Selections.Should().Be(0);
    }

    [Fact]
    public async Task A_reason_longer_than_the_limit_is_refused()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", ModelTakingPresses().Answer));
        var arguments = Pressing(Capped);
        arguments["reason"] = new string('a', 501);

        (await server.Client.CallToolAsync("press", arguments)).IsError.Should().BeTrue();
    }

    [Fact]
    public async Task A_reason_at_the_limit_with_spaces_around_it_is_taken()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", ModelTakingPresses().Answer));
        var arguments = Pressing(Capped);
        arguments["reason"] = "  " + new string('a', PressTools.LongestReason) + "  ";

        var answer = await server.Client.CallToolAsync("press", arguments);
        answer.IsError.Should().NotBe(true, TextOf(answer));
    }

    [Fact]
    public async Task A_report_Mycelium_does_not_take_does_not_fail_the_press()
    {
        var model = ModelTakingPresses(takingReports: false);
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await PressedAsync(server, Capped);

        answer.GetProperty("sent").GetBoolean().Should().BeTrue();
        answer.GetProperty("reported").GetBoolean().Should().BeFalse();
        model.Writes.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_write_Mycelium_refuses_fails_the_press_and_nothing_after_it_is_sent()
    {
        var model = ModelTakingPresses(takingWrites: false);
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await server.Client.CallToolAsync("press", Pressing(Capped, preview: false, digest: await DigestOfAsync(server, Capped)));

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("nothing after the refused write was sent");
        model.Writes.Select(write => write.Route).Should().Equal("POST /api/relationships");
    }

    [Fact]
    public async Task A_choice_that_asks_a_secret_is_refused_and_the_secret_is_neither_sent_nor_shown()
    {
        var choice = new JsonObject
        {
            ["label"] = "Set the gate code", ["act"] = "setCode", ["via"] = "gates",
            ["asks"] = new JsonArray(new JsonObject { ["key"] = "gateCode", ["label"] = "Code", ["kind"] = "secret" }),
        };
        var model = new HandMadeModelTakingPresses(choice);
        await using var server = await StartAsync(MyceliumFor(() => "editor", model.Answer));

        var answer = await server.Client.CallToolAsync("press", new Dictionary<string, object?>
        {
            ["page"] = HandMadePage, ["widget"] = HandMadeModelTakingPresses.Widget, ["row"] = NorthPond, ["choice"] = "listed:0",
            ["answers"] = new Dictionary<string, object> { ["gateCode"] = "4711-never-shown" }, ["reason"] = Reason, ["preview"] = false,
        });

        answer.IsError.Should().BeTrue();
        TextOf(answer).Should().Contain("secret");
        server.SentToTheChatApplication.Should().NotContain("4711-never-shown");
        model.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/model")]
    [InlineData("/api/mycelium/shutdown")]
    [InlineData("/api/auth/administration")]
    [InlineData("/api/endpoints/../model")]
    [InlineData("//elsewhere.example/api/endpoints/gates")]
    [InlineData("../../model")]
    public async Task A_choice_whose_route_is_off_the_list_is_never_posted_to(string via)
    {
        var model = new HandMadeModelTakingPresses(new JsonObject { ["label"] = "Open", ["act"] = "open", ["via"] = via });
        await using var server = await StartAsync(MyceliumFor(() => "admin", model.Answer));

        var answer = await server.Client.CallToolAsync("press", HandMadePress(await HandMadeDigestAsync(server)));

        answer.IsError.Should().BeTrue();
        model.Writes.Should().BeEmpty();
        server.Mycelium.Requests.Should().OnlyContain(request => request.RequestUri!.Host == new Uri(MyceliumUrl).Host);
    }

    [Fact]
    public async Task A_choice_whose_route_is_a_service_endpoint_is_posted_to()
    {
        var model = new HandMadeModelTakingPresses(new JsonObject { ["label"] = "Open", ["act"] = "open", ["via"] = "gates" });
        await using var server = await StartAsync(MyceliumFor(() => "admin", model.Answer));

        var answer = await server.Client.CallToolAsync("press", HandMadePress(await HandMadeDigestAsync(server)));

        answer.IsError.Should().NotBe(true, TextOf(answer));
        model.Writes.Select(write => write.Route).Should().Equal("POST /api/endpoints/gates");
    }

    [Fact]
    public async Task A_press_whose_stamp_names_no_identifier_sends_none_of_its_writes()
    {
        var model = new HandMadeModelTakingPresses(new JsonObject { ["label"] = "Open", ["act"] = "open", ["via"] = "gates" }, rowId: "../model", stamps: "openedAt");
        await using var server = await StartAsync(MyceliumFor(() => "admin", model.Answer));

        var arguments = HandMadePress(digest: null);
        arguments["row"] = "../model";
        arguments["confirm_digest"] = Parsed(await server.Client.CallToolAsync("press", WithPreview(arguments))).GetProperty("digest").GetString();
        var answer = await server.Client.CallToolAsync("press", arguments);

        answer.IsError.Should().BeTrue();
        model.Writes.Should().BeEmpty("a press is checked whole before any of its writes is sent");
        TextOf(answer).Should().Contain("\"../model\"");
    }

    [Fact]
    public async Task A_preview_of_a_press_that_would_not_be_sent_says_why()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor", ModelTakingPresses().Answer));

        var previewed = Parsed(await server.Client.CallToolAsync("press", Pressing("listed:2")));

        previewed.GetProperty("wouldNotBeSent").GetString().Should().Contain("/api/operator-activity");
    }

    [Fact]
    public void The_press_tools_are_listed_under_the_role_the_reader_finds_pages_by() =>
        PageRoles.Page.Should().Be("a18768bc-b4e7-513c-864f-842d674c1858");

    // A report's description is itself sent as JSON, which writes the quotes around the reason as ".
    private static string QuotedInAReport(string text) => $"\\u0022{text}\\u0022";

    private static Dictionary<string, object?> Pressing(
        string choice, bool preview = true, string? digest = null, Dictionary<string, object>? answers = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["page"] = s_catchmentsPage, ["widget"] = Springs, ["row"] = ColdSpring, ["choice"] = choice, ["reason"] = Reason, ["preview"] = preview,
        };
        if (answers is not null) arguments["answers"] = answers;
        if (digest is not null) arguments[Confirmation.DigestParameter] = digest;
        return arguments;
    }

    private static async Task<string> DigestOfAsync(ServerUnderTest server, string choice, Dictionary<string, object>? answers = null) =>
        Parsed(await server.Client.CallToolAsync("press", Pressing(choice, answers: answers))).GetProperty("digest").GetString()!;

    private static async Task<JsonElement> PressedAsync(ServerUnderTest server, string choice, Dictionary<string, object>? answers = null)
    {
        var digest = await DigestOfAsync(server, choice, answers);
        return Parsed(await server.Client.CallToolAsync("press", Pressing(choice, preview: false, digest: digest, answers: answers)));
    }

    private static Dictionary<string, object?> HandMadePress(string? digest)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["page"] = HandMadePage, ["widget"] = HandMadeModelTakingPresses.Widget, ["row"] = NorthPond, ["choice"] = "listed:0",
            ["reason"] = Reason, ["preview"] = false,
        };
        if (digest is not null) arguments[Confirmation.DigestParameter] = digest;
        return arguments;
    }

    private static Dictionary<string, object?> WithPreview(Dictionary<string, object?> arguments) =>
        new(arguments) { ["preview"] = true, [Confirmation.DigestParameter] = null };

    private static async Task<string> HandMadeDigestAsync(ServerUnderTest server) =>
        Parsed(await server.Client.CallToolAsync("press", WithPreview(HandMadePress(digest: null)))).GetProperty("digest").GetString()!;

    private static JsonElement Parsed(CallToolResult result)
    {
        result.IsError.Should().NotBe(true, TextOf(result));
        return JsonDocument.Parse(TextOf(result)).RootElement;
    }

    private static RecordedModelTakingPresses ModelTakingPresses(bool bindsThePageRole = true, bool takingReports = true, bool takingWrites = true) =>
        new(bindsThePageRole, takingReports, takingWrites);

    private sealed class RecordedModelTakingPresses(bool bindsThePageRole, bool takingReports, bool takingWrites)
    {
        public List<(string Route, string Body)> Writes { get; } = [];
        public List<string> Reports { get; } = [];
        public int Selections { get; private set; }

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            if (path == "/api/snapshots") Selections++;
            if (request.Method == HttpMethod.Get && path == "/api/things" && request.RequestUri.Query.Contains("boundBy=" + PageRoles.Page))
                return bindsThePageRole ? Json($$"""{"Id":"{{PageRoles.Page}}","Name":"Page"}""") : new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path == "/api/operator-activity")
            {
                Reports.Add(body);
                return takingReports ? new HttpResponseMessage(HttpStatusCode.Accepted) : Json("""{"error":"Not taken"}""", HttpStatusCode.Forbidden);
            }
            if (path == "/api/relationships" && request.Method == HttpMethod.Post)
            {
                Writes.Add(($"POST {path}", body));
                return takingWrites
                    ? Json("""{"Id":"00000000-0000-4000-9000-000000009999"}""")
                    : Json("""{"error":"This key may not relate these Things"}""", HttpStatusCode.Forbidden);
            }
            if (request.Method == HttpMethod.Put || (request.Method == HttpMethod.Post && path.StartsWith("/api/endpoints/", StringComparison.Ordinal)))
            {
                Writes.Add(($"{request.Method} {path}", body));
                return Json(path.StartsWith("/api/endpoints/", StringComparison.Ordinal) ? """{"said":"A flow test is booked"}""" : $$"""{"Id":"{{ColdSpring}}"}""");
            }
            return MyceliumAnsweringFromTheRecording.Answer(request);
        }
    }

    private sealed class HandMadeModelTakingPresses(JsonObject choice, string rowId = NorthPond, string? stamps = null)
    {
        public const string Widget = "Gates to the reservoirs";

        public List<(string Route, string Body)> Writes { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/things" && request.RequestUri.Query.Contains("boundBy="))
                return Json($$"""{"Id":"{{PageRoles.Page}}","Name":"Page"}""");
            if (path == "/api/snapshots") return Json($$"""{"watermark":1,"snapshot":{{Selection()}}}""");
            if (path == $"/api/states/{Waiting}/things")
                return Json(JsonSerializer.Serialize(new { StateName = Waiting, Things = new[] { new { Id = rowId, Name = "North pond" } } }));
            if (path == "/api/operator-activity") return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(($"{request.Method} {path}", request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
                return Json("{}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string Selection()
        {
            var writes = new JsonObject { ["choices"] = new JsonArray(choice.DeepClone()) };
            if (stamps is not null) writes["stamps"] = stamps;
            var description = new JsonObject
            {
                ["title"] = "Gates",
                ["sections"] = new JsonArray(new JsonObject
                {
                    ["widgets"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "action", ["title"] = Widget,
                        ["rows"] = new JsonObject { ["kind"] = "stateList", ["state"] = Waiting },
                        ["writes"] = writes,
                    }),
                }),
            };
            return HandMadePages.Selection((HandMadePage, "Gates", description.ToJsonString()));
        }
    }
}
