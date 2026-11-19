using System.Text.Json;
using vos.Tests.Shared;
using Xunit;

namespace vos.Pages.Tests;

public class PageReaderTests
{
    private const string English = "en";
    private const string Swedish = "sv";
    private static readonly CancellationToken s_none = CancellationToken.None;

    [Fact]
    public async Task Pages_are_listed_by_name_and_one_whose_description_cannot_be_read_is_still_listed()
    {
        using var reader = new PageReader();

        var pages = await reader.ListPagesAsync(English, new ReadsAnsweredFromMemory(), s_none);

        Assert.Equal(
            [
                new PageListing(HandBuiltModel.UnreadablePageId, "Catchment inflow", "catchment-inflow", Readable: false),
                new PageListing(HandBuiltModel.ReservoirLevelsPageId, "Reservoir levels", "reservoir-levels", Readable: true),
            ],
            pages);
    }

    [Fact]
    public async Task The_Things_a_page_can_be_read_for_are_the_ones_it_compares_by_name()
    {
        using var reader = new PageReader();

        var scopes = await reader.ScopesOfAsync(HandBuiltModel.ReservoirLevelsPageId, English, new ReadsAnsweredFromMemory(), s_none);

        Assert.Equal(
            [
                new PageScope(HandBuiltModel.AsenReservoirId, "Åsen reservoir"),
                new PageScope(HandBuiltModel.OstraReservoirId, "Östra reservoir"),
                new PageScope(HandBuiltModel.SpringReservoirId, "Spring reservoir"),
            ],
            scopes);
    }

    [Fact]
    public async Task A_page_read_for_one_Thing_gives_that_Things_own_value()
    {
        using var reader = new PageReader();

        var read = await ReadOneAsync(reader, new ReadsAnsweredFromMemory(), HandBuiltModel.OstraReservoirId);

        Assert.Equal(300, ResultOf(read, "Capacity").GetDouble());
    }

    [Fact]
    public async Task A_page_read_for_no_Thing_gives_the_average_over_the_Things_it_compares()
    {
        using var reader = new PageReader();

        var read = await ReadOneAsync(reader, new ReadsAnsweredFromMemory(), scopeId: null);

        Assert.Equal(200, ResultOf(read, "Capacity").GetDouble());
    }

    [Fact]
    public async Task What_the_loaded_Things_cannot_answer_is_asked_of_the_reads_and_put_into_the_page()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory { ReservoirsRunningLow = 2 };

        var read = await ReadOneAsync(reader, reads, scopeId: null);

        Assert.Equal(2, ResultOf(read, "Reservoirs running low").GetInt32());
        Assert.Equal($"/api/states/{HandBuiltModel.RunningLow}/things?countOnly=true", Assert.Single(reads.Got));
    }

    [Fact]
    public async Task Names_outside_the_English_alphabet_are_ordered_the_same_on_every_machine()
    {
        using var reader = new PageReader();

        var read = await ReadOneAsync(reader, new ReadsAnsweredFromMemory(), scopeId: null);

        var names = ResultOf(read, "Reservoirs").EnumerateArray().Select(row => row.GetProperty("name").GetString());
        Assert.Equal(["Åsen reservoir", "Östra reservoir", "Spring reservoir"], names);
    }

    [Fact]
    public async Task Names_are_ordered_as_the_language_asked_for_orders_them()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory();

        var read = JsonDocument.Parse(Assert.Single(
            await reader.ReadPageAsync(HandBuiltModel.ReservoirLevelsPageId, [null], Swedish, reads, s_none))).RootElement;
        var scopes = await reader.ScopesOfAsync(HandBuiltModel.ReservoirLevelsPageId, Swedish, reads, s_none);

        string[] asSwedishOrdersThem = ["Spring reservoir", "Åsen reservoir", "Östra reservoir"];
        Assert.Equal(asSwedishOrdersThem, ResultOf(read, "Reservoirs").EnumerateArray().Select(row => row.GetProperty("name").GetString()));
        Assert.Equal(asSwedishOrdersThem, scopes.Select(scope => scope.Name));
    }

    [Fact]
    public async Task A_page_read_for_several_Things_fetches_its_Things_once()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory();
        string?[] scopes = [null, HandBuiltModel.AsenReservoirId, HandBuiltModel.OstraReservoirId, HandBuiltModel.SpringReservoirId];

        var answers = await reader.ReadPageAsync(HandBuiltModel.ReservoirLevelsPageId, scopes, English, reads, s_none);

        Assert.Equal([200, 100, 300, 200], answers.Select(answer => ResultOf(JsonDocument.Parse(answer).RootElement, "Capacity").GetDouble()));
        Assert.Equal(2, reads.Selectors.Count);
        var pageSelector = JsonDocument.Parse(reads.Selectors.Last()).RootElement;
        var selectedIds = pageSelector.GetProperty("ids").EnumerateArray().Select(id => id.GetString()).ToList();
        Assert.Contains(HandBuiltModel.AsenReservoirId, selectedIds);
        Assert.Contains(HandBuiltModel.OstraReservoirId, selectedIds);
        Assert.Contains(HandBuiltModel.SpringReservoirId, selectedIds);
    }

    [Fact]
    public async Task Page_reads_waiting_on_the_model_at_the_same_time_give_the_answers_they_give_one_after_another()
    {
        using var reader = new PageReader();
        string?[] scopes = [null, HandBuiltModel.AsenReservoirId, HandBuiltModel.OstraReservoirId, HandBuiltModel.SpringReservoirId];
        var oneAfterAnother = new List<string>();
        foreach (var scope in scopes)
            oneAfterAnother.Add((await reader.ReadPageAsync(HandBuiltModel.ReservoirLevelsPageId, [scope], English, new ReadsAnsweredFromMemory(), s_none))[0]);

        var everyReadIsWaiting = new TaskCompletionSource();
        var heldReads = new ReadsAnsweredFromMemory { EachReadWaitsFor = everyReadIsWaiting.Task };
        var together = scopes
            .Select(scope => Task.Run(() => reader.ReadPageAsync(HandBuiltModel.ReservoirLevelsPageId, [scope], English, heldReads, s_none)))
            .ToList();
        await Settle.UntilAsync(() => heldReads.Got.Count == scopes.Length, "every page read is waiting on its read of the state");
        everyReadIsWaiting.SetResult();

        Assert.Equal(oneAfterAnother, (await Task.WhenAll(together)).Select(answers => answers[0]));
    }

    [Fact]
    public async Task Nothing_is_kept_between_calls_so_memory_returns_to_where_it_started()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory();
        await ReadOneAsync(reader, reads, HandBuiltModel.AsenReservoirId);
        var afterTheFirstCall = reader.EngineMemoryInUseMebibytes();

        for (var call = 0; call < 200; call++)
        {
            await reader.ListPagesAsync(English, reads, s_none);
            await ReadOneAsync(reader, reads, HandBuiltModel.AsenReservoirId);
        }

        Assert.InRange(reader.EngineMemoryInUseMebibytes(), 0, afterTheFirstCall + 1);
    }

    [Fact]
    public async Task A_field_a_widget_asks_a_person_to_fill_is_not_read_as_a_binding()
    {
        using var reader = new PageReader();

        var read = await ReadOneAsync(reader, new ReadsAnsweredFromMemory(), scopeId: null);

        var kinds = WidgetTitled(read, "Reservoirs to inspect").GetProperty("slots").EnumerateArray().Select(binding => binding.GetProperty("kind").GetString());
        Assert.Equal(["thingList"], kinds);
    }

    [Fact]
    public async Task A_question_the_reader_warns_about_resolves_to_nothing_and_the_warning_reaches_the_caller()
    {
        var warnings = new List<string>();
        using var reader = new PageReader(new PageReaderOptions { Warned = warnings.Add });

        var read = await ReadOneAsync(reader, new ReadsAnsweredFromMemory(), scopeId: null);

        Assert.Equal(JsonValueKind.Null, ResultOf(read, "Inflow asked the wrong way round").ValueKind);
        Assert.Contains("timeseries over Reservoir", Assert.Single(warnings));
    }

    [Fact]
    public async Task A_page_that_is_not_among_the_models_pages_is_refused_by_its_identifier()
    {
        using var reader = new PageReader();
        const string unknown = "00000000-0000-4000-8000-00000000ffff";

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadPageAsync(unknown, [null], English, new ReadsAnsweredFromMemory(), s_none));

        Assert.Contains(unknown, refused.Message);
    }

    [Fact]
    public async Task A_page_whose_description_cannot_be_read_is_refused_by_its_name()
    {
        using var reader = new PageReader();

        var refused = await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadPageAsync(HandBuiltModel.UnreadablePageId, [null], English, new ReadsAnsweredFromMemory(), s_none));

        Assert.Contains("Catchment inflow", refused.Message);
    }

    private static async Task<JsonElement> ReadOneAsync(PageReader reader, IPageReads reads, string? scopeId)
    {
        var answers = await reader.ReadPageAsync(HandBuiltModel.ReservoirLevelsPageId, [scopeId], English, reads, s_none);
        return JsonDocument.Parse(Assert.Single(answers)).RootElement;
    }

    private static JsonElement ResultOf(JsonElement page, string widgetTitle)
    {
        var widget = WidgetTitled(page, widgetTitle);
        return widget.TryGetProperty("value", out var figure) ? figure : widget.GetProperty("slots")[0].GetProperty("result");
    }

    private static JsonElement WidgetTitled(JsonElement page, string title) =>
        page.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("widgets").EnumerateArray())
            .Single(widget => widget.GetProperty("title").GetString() == title);
}
