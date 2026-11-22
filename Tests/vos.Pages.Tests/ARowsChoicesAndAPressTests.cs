using System.Text.Json;
using Xunit;

namespace vos.Pages.Tests;

public class ARowsChoicesAndAPressTests
{
    private const string English = "en";
    private const string CatchmentsPage = "ad9d2665-b93d-5447-9d9e-39ea6cb1e085";
    private const string Springs = "Springs waiting for a decision";
    private const string ColdSpring = "d5abcde5-800f-578f-8cb8-89b66825f468";
    private const string DecidedAs = "ae41e56e-ac72-5396-9c35-657dcb561b1c";
    private const string Capped = "2420729f-9c3e-5e20-a37c-41bfaaea0e39";
    private const string Left = "2ab3fd24-8196-5c5e-8007-7883d6bc293f";
    private const string FlowTest = "listed:1";
    private const string RecordAsReviewed = "listed:2";
    private static readonly CancellationToken s_none = CancellationToken.None;

    [Fact]
    public async Task A_row_is_offered_the_models_choices_in_its_language_and_each_listed_choice_by_its_place()
    {
        using var reader = new PageReader();

        var offered = await OfferedOnColdSpringAsync(reader, "de");

        var choices = offered.GetProperty("choices").EnumerateArray()
            .ToDictionary(choice => choice.GetProperty("key").GetString()!, choice => choice.GetProperty("label").GetString()!);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["listed:0"] = "Tap for the reservoir",
                [Capped] = "Verschließen",
                [Left] = "Belassen",
                [FlowTest] = "Ask for a flow test",
                [RecordAsReviewed] = "Record as reviewed",
            },
            choices);
        Assert.Equal(ColdSpring, offered.GetProperty("row").GetProperty("id").GetString());
        Assert.Equal("Cold spring", offered.GetProperty("row").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_choice_the_model_offers_says_which_Thing_it_stands_for()
    {
        using var reader = new PageReader();

        var capped = ChoiceKeyed(await OfferedOnColdSpringAsync(reader, English), Capped);

        Assert.Equal(Capped, capped.GetProperty("standsFor").GetProperty("id").GetString());
        Assert.Equal("capped", capped.GetProperty("standsFor").GetProperty("name").GetString());
        Assert.Empty(capped.GetProperty("asks").EnumerateArray());
        Assert.True(capped.GetProperty("pressable").GetBoolean());
    }

    [Fact]
    public async Task A_choice_lists_what_it_asks_and_the_Things_a_field_is_chosen_from_for_that_row()
    {
        using var reader = new PageReader();

        var flowTest = ChoiceKeyed(await OfferedOnColdSpringAsync(reader, English), FlowTest);

        var asks = flowTest.GetProperty("asks").EnumerateArray().ToList();
        Assert.Equal(["days", "reservoir", "note"], asks.Select(field => field.GetProperty("key").GetString()));
        Assert.Equal("number", asks[0].GetProperty("kind").GetString());
        Assert.True(asks[2].GetProperty("optional").GetBoolean());
        var reservoirs = asks[1].GetProperty("from").EnumerateArray().ToList();
        Assert.Equal(
            ["Meadow pond", "North cistern", "North pond", "South pond", "Terrace cistern"],
            reservoirs.Select(reservoir => reservoir.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.All(reservoirs, reservoir => Assert.True(reservoir.TryGetProperty("capacityM3", out _)));
    }

    [Fact]
    public async Task A_row_given_by_its_name_is_the_row_given_by_its_identifier()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();

        var byName = await reader.OfferedChoicesAsync(CatchmentsPage, Springs, null, "Cold spring", English, model, s_none);

        Assert.Equal(JsonDocument.Parse(await reader.OfferedChoicesAsync(CatchmentsPage, Springs, null, ColdSpring, English, model, s_none)).RootElement.ToString(),
            JsonDocument.Parse(byName).RootElement.ToString());
    }

    [Theory]
    [InlineData("Old spring")]
    [InlineData("f2a57e59-064c-5215-a563-f00dc8b91d19")]
    [InlineData("A spring nobody named")]
    public async Task A_row_the_widget_does_not_list_is_refused_with_what_was_asked(string row)
    {
        using var reader = new PageReader();

        var refused = await Assert.ThrowsAsync<PageNotReadException>(() =>
            reader.OfferedChoicesAsync(CatchmentsPage, Springs, null, row, English, RecordedModel.OfTheCatchmentsSeed(), s_none));

        Assert.EndsWith(JsonSerializer.Serialize(row), refused.Message);
    }

    [Theory]
    [InlineData("A widget nobody titled")]
    [InlineData("Every catchment")]
    public async Task Only_an_action_widget_of_the_page_offers_choices(string widget)
    {
        using var reader = new PageReader();

        var refused = await Assert.ThrowsAsync<PageNotReadException>(() =>
            reader.OfferedChoicesAsync(CatchmentsPage, widget, null, ColdSpring, English, RecordedModel.OfTheCatchmentsSeed(), s_none));

        Assert.EndsWith(JsonSerializer.Serialize(widget), refused.Message);
    }

    [Fact]
    public async Task A_press_on_a_choice_the_model_offers_relates_the_row_to_it_and_then_stamps_the_row()
    {
        using var reader = new PageReader();

        var composed = await ComposedOnColdSpringAsync(reader, Capped, "{}");

        Assert.Equal(
            JsonSerializer.Serialize(new object[]
            {
                new { write = "relate", predicate = "decidedAs", subjectId = ColdSpring, predicateId = DecidedAs, targetId = Capped },
                new { write = "stamp", thingId = ColdSpring, property = "decidedAt" },
            }),
            composed.GetProperty("writes").GetRawText());
    }

    [Fact]
    public async Task A_press_on_a_choice_with_its_own_route_posts_the_row_by_name_and_what_was_asked_then_stamps()
    {
        using var reader = new PageReader();

        var composed = await ComposedOnColdSpringAsync(reader, FlowTest, """{"days": "3", "reservoir": "North pond", "note": ""}""");

        Assert.Equal(
            JsonSerializer.Serialize(new object[]
            {
                new { write = "post", via = "echo", body = new { view = "flowTest", record = "Cold spring", days = 3, reservoir = "North pond" } },
                new { write = "stamp", thingId = ColdSpring, property = "decidedAt" },
            }),
            composed.GetProperty("writes").GetRawText());
    }

    [Fact]
    public async Task A_press_posted_to_a_platform_route_also_carries_the_rows_identifier()
    {
        using var reader = new PageReader();

        var composed = await ComposedOnColdSpringAsync(reader, RecordAsReviewed, "{}");

        Assert.Equal(
            JsonSerializer.Serialize(new { write = "post", via = "/api/operator-activity", body = new { view = "reviewed", record = "Cold spring", recordId = ColdSpring } }),
            composed.GetProperty("writes")[0].GetRawText());
    }

    [Theory]
    [InlineData("""{"reservoir": "North pond"}""", "\"days\"")]
    [InlineData("""{"days": "3", "reservoir": "North pond", "litres": "40"}""", "\"litres\"")]
    [InlineData("""{"days": "3", "reservoir": "A pond the model does not hold"}""", "\"A pond the model does not hold\"")]
    public async Task A_press_is_refused_an_answer_missing_one_not_asked_for_or_one_its_field_does_not_offer(string answers, string named)
    {
        using var reader = new PageReader();

        var refused = await Assert.ThrowsAsync<PageNotReadException>(() => ComposedOnColdSpringAsync(reader, FlowTest, answers));

        Assert.Contains(named, refused.Message);
    }

    [Fact]
    public async Task A_press_on_a_choice_the_row_is_not_offered_is_refused_with_the_key_asked()
    {
        using var reader = new PageReader();

        var refused = await Assert.ThrowsAsync<PageNotReadException>(() => ComposedOnColdSpringAsync(reader, "listed:9", "{}"));

        Assert.EndsWith("\"listed:9\"", refused.Message);
    }

    [Fact]
    public async Task A_choice_that_asks_a_secret_is_offered_as_not_pressable_and_a_press_on_it_never_reads_the_answers()
    {
        using var reader = new PageReader();
        var model = new ReadsAnsweredFromMemory();

        var offered = JsonDocument.Parse(await reader.OfferedChoicesAsync(
            HandBuiltModel.ReservoirLevelsPageId, HandBuiltModel.GateWidget, null, HandBuiltModel.AsenReservoirId, English, model, s_none)).RootElement;
        var refused = await Assert.ThrowsAsync<PageNotReadException>(() => reader.ComposePressAsync(
            HandBuiltModel.ReservoirLevelsPageId, HandBuiltModel.GateWidget, null, HandBuiltModel.AsenReservoirId, "listed:0",
            """{"gateCode": "4711-secret"}""", English, model, s_none));

        var choice = Assert.Single(offered.GetProperty("choices").EnumerateArray());
        Assert.False(choice.GetProperty("pressable").GetBoolean());
        Assert.Contains("secret", choice.GetProperty("notPressable").GetString());
        Assert.DoesNotContain("4711-secret", refused.Message);
        Assert.Contains("secret", refused.Message);
    }

    [Fact]
    public async Task The_Things_a_widgets_own_field_is_chosen_from_are_read_once_however_many_choices_ask_it()
    {
        using var reader = new PageReader();
        var model = new ReadsAnsweredFromMemory();

        var offered = JsonDocument.Parse(await reader.OfferedChoicesAsync(
            HandBuiltModel.ReservoirsToDrainPageId, HandBuiltModel.DrainWidget, null, HandBuiltModel.AsenReservoirId, English, model, s_none)).RootElement;

        Assert.Equal(3, offered.GetProperty("choices").GetArrayLength());
        Assert.All(offered.GetProperty("choices").EnumerateArray(), choice => Assert.Equal("into", Assert.Single(choice.GetProperty("asks").EnumerateArray()).GetProperty("key").GetString()));
        Assert.Single(model.Got, path => path.StartsWith($"/api/states/{HandBuiltModel.RunningLow}/things", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_sent_writes_go_out_in_order_the_post_is_reported_and_what_the_endpoint_said_is_answered()
    {
        using var reader = new PageReader();
        var writes = new WritesKeptInMemory { PressAnswer = """{"said": "A flow test is booked"}""" };
        var composed = await ComposedOnColdSpringAsync(reader, FlowTest, """{"days": "3", "reservoir": "North pond"}""");

        var answer = JsonDocument.Parse(await reader.SendPressAsync(composed.GetProperty("writes").GetRawText(), writes)).RootElement;

        Assert.Equal("A flow test is booked", answer.GetProperty("said").GetString());
        Assert.Equal(["press /api/endpoints/echo", $"stamp {ColdSpring} decidedAt"], writes.Made);
        Assert.Equal([("send a form to echo", true)], writes.Reported);
        Assert.True(DateTimeOffset.TryParse(writes.StampedAt.Single(), out _));
    }

    [Fact]
    public async Task A_refused_relate_is_answered_in_its_own_words_and_the_row_is_not_stamped()
    {
        using var reader = new PageReader();
        var writes = new WritesKeptInMemory { RelateRefusal = "The relationship already exists" };
        var composed = await ComposedOnColdSpringAsync(reader, Capped, "{}");

        var answer = JsonDocument.Parse(await reader.SendPressAsync(composed.GetProperty("writes").GetRawText(), writes)).RootElement;

        Assert.Contains("The relationship already exists", answer.GetProperty("error").GetString());
        Assert.Equal([$"relate {ColdSpring} {DecidedAs} {Capped}"], writes.Made);
    }

    [Fact]
    public async Task The_page_role_the_server_lists_its_press_tools_under_is_the_one_the_reader_finds_pages_by()
    {
        using var reader = new PageReader();

        Assert.Equal(PageRoles.Page, reader.PageRoleTheReaderUses());
    }

    private static async Task<JsonElement> OfferedOnColdSpringAsync(PageReader reader, string language) =>
        JsonDocument.Parse(await reader.OfferedChoicesAsync(CatchmentsPage, Springs, null, ColdSpring, language, RecordedModel.OfTheCatchmentsSeed(), s_none)).RootElement;

    private static async Task<JsonElement> ComposedOnColdSpringAsync(PageReader reader, string choice, string answersJson) =>
        JsonDocument.Parse(await reader.ComposePressAsync(
            CatchmentsPage, Springs, null, ColdSpring, choice, answersJson, English, RecordedModel.OfTheCatchmentsSeed(), s_none)).RootElement;

    private static JsonElement ChoiceKeyed(JsonElement offered, string key) =>
        Assert.Single(offered.GetProperty("choices").EnumerateArray(), choice => choice.GetProperty("key").GetString() == key);
}
