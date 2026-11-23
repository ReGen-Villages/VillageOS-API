using System.Text.Json;
using Xunit;

namespace vos.Pages.Tests;

// The library against Things and answers a running Mycelium gave: every page must come out as the
// same reader resolved it in Node from the same answers.
public class APageReadFromTheRecordedModelTests
{
    private const string English = "en";
    private static readonly CancellationToken s_none = CancellationToken.None;

    [Fact]
    public async Task Every_page_read_for_each_Thing_it_compares_is_what_the_reader_in_Node_resolved()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();
        var compared = 0;

        foreach (var page in model.Pages)
        {
            foreach (var scopeId in page.Scopes.Select(scope => (string?)scope.Id).Prepend(null))
            {
                var read = Assert.Single(await reader.ReadPageAsync(page.Id, [scopeId], English, model, s_none));
                AssertSameJson(page.AnswerFor(scopeId), read);
                compared++;
            }
        }

        Assert.True(compared > 1, "the recording holds a page read for at least one Thing as well as for none");
    }

    [Fact]
    public async Task A_page_read_for_all_its_Things_at_once_answers_each_as_it_does_alone()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();
        var page = model.Pages.First();
        var scopes = page.Scopes.Select(scope => (string?)scope.Id).ToList();

        var together = await reader.ReadPageAsync(page.Id, scopes, English, model, s_none);

        Assert.Equal(scopes.Count, together.Count);
        for (var position = 0; position < scopes.Count; position++) AssertSameJson(page.AnswerFor(scopes[position]), together[position]);
    }

    [Fact]
    public async Task The_pages_and_the_Things_a_page_compares_are_listed_as_the_reader_in_Node_listed_them()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();

        Assert.Equal(model.Listed, await reader.ListPagesAsync(English, model, s_none));
        Assert.Contains(model.Listed, listed => !listed.Readable);
        foreach (var page in model.Pages) Assert.Equal(page.Scopes, await reader.ScopesOfAsync(page.Id, English, model, s_none));
    }

    [Fact]
    public async Task A_binding_whose_service_does_not_answer_reads_as_nothing_and_the_rest_of_the_page_still_reads()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();
        var page = model.Pages.First();

        var read = JsonDocument.Parse(Assert.Single(await reader.ReadPageAsync(page.Id, [null], English, model, s_none))).RootElement;

        var widgets = read.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("widgets").EnumerateArray())
            .ToList();
        var askingTheService = Assert.Single(widgets, widget => widget.TryGetProperty("title", out var title) && title.GetString() == "Service answer");
        Assert.Equal(JsonValueKind.Null, askingTheService.GetProperty("value").ValueKind);
        Assert.Equal("—", askingTheService.GetProperty("worded").GetString());
        Assert.Contains(widgets, widget => widget.TryGetProperty("worded", out var worded) && worded.GetString() != "—");
        Assert.DoesNotContain(widgets, widget => widget.TryGetProperty("unreadable", out _));
    }

    [Fact]
    public async Task The_inputs_the_origins_and_the_explanation_of_each_Things_figures_are_what_the_reader_in_Node_answered()
    {
        using var reader = new PageReader();
        var model = RecordedModel.OfTheCatchmentsSeed();
        var explained = 0;

        foreach (var thing in model.Things)
        {
            AssertSameJson(thing.Inputs, await reader.InputsOfAsync(thing.Id, figure: null, English, model, s_none));
            foreach (var (figure, answer) in thing.Explained)
            {
                AssertSameJson(answer, await reader.ExplainAsync(thing.Id, figure, English, model, s_none));
                explained++;
            }
            foreach (var (property, origin) in thing.Origins) AssertSameJson(origin, await reader.OriginOfAsync(thing.Id, property, model, s_none));
        }

        Assert.True(explained > 0, "the recording holds at least one Thing with a derived figure");
    }

    private static void AssertSameJson(JsonElement expected, string actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement));
}
