using System.Text.Json;
using Xunit;

namespace vos.Pages.Tests;

/// <summary>
/// The library against Things and answers a running Mycelium gave: every page must come out as the
/// same reader resolved it in Node from the same answers.
/// </summary>
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

        var bindings = read.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("widgets").EnumerateArray())
            .SelectMany(widget => widget.GetProperty("bindings").EnumerateArray())
            .ToList();
        var fromTheService = Assert.Single(bindings, binding => binding.GetProperty("kind").GetString() == "service");
        Assert.Equal(JsonValueKind.Null, fromTheService.GetProperty("result").ValueKind);
        Assert.Contains(bindings, binding => binding.GetProperty("kind").GetString() == "thingList" && binding.GetProperty("result").GetArrayLength() > 0);
    }

    private static void AssertSameJson(JsonElement expected, string actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(JsonDocument.Parse(actual).RootElement));
}
