using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

// The route a public form draws itself from. It demands no credential, for the same reason the submission
// route demands none: the page asking is a stranger's browser, and there is nothing for it to hold.
public class FormOptionsEndpointTests
{
    private static IntakeWebApplicationFactory AnsweringWith(SnapshotDocument model) => new()
    {
        HandlerCallback = _ => ModelStub.Json(JsonSerializer.Serialize(
            new SubscribeResult(Guid.NewGuid(), 0, model),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))),
    };

    [Fact]
    public async Task It_answers_a_caller_holding_no_credential()
    {
        await using var factory = AnsweringWith(DeclaredModel.Seeded().Build());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/submissions/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task It_answers_the_categories_and_the_basemap_the_model_declares()
    {
        await using var factory = AnsweringWith(DeclaredModel.Seeded().Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        answered.GetProperty("allocationCategories").EnumerateArray()
            .Select(category => category.GetString()).Should()
            .BeEquivalentTo(WillowBend.AllocationCategoryNames);
        answered.GetProperty("basemapSources").EnumerateArray()
            .Select(source => source.GetProperty("name").GetString()).Should()
            .Contain(WillowBend.VectorBasemapName);
    }

    // The split a page offers before anybody has stated a programme, and whether the position lookups
    // are registered — both the model's answer, so a page draws only what the model can honour.
    [Fact]
    public async Task It_answers_the_starting_programme_and_which_lookups_are_registered()
    {
        await using var factory = AnsweringWith(DeclaredModel.Seeded().Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        answered.GetProperty("defaultProgramme").EnumerateArray()
            .Select(share => (share.GetProperty("category").GetString(),
                share.GetProperty("sharePct").GetDouble()))
            .Should().BeEquivalentTo(
                WillowBend.DefaultProgramme.Select(share => (share.Category, share.SharePct)));
        answered.GetProperty("parcelLookup").GetBoolean().Should().BeTrue();
        answered.GetProperty("placeSearch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_model_registering_no_lookups_and_no_defaults_offers_a_page_that_starts_blank()
    {
        var model = DeclaredModel.Seeded()
            .Without(WillowBend.ParcelRegisterName)
            .Without(WillowBend.PlaceSearchName);
        foreach (var (category, _) in WillowBend.DefaultProgramme)
            model.Stating(category);
        await using var factory = AnsweringWith(model.Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        answered.GetProperty("defaultProgramme").EnumerateArray().Should().BeEmpty();
        answered.GetProperty("parcelLookup").GetBoolean().Should().BeFalse();
        answered.GetProperty("placeSearch").GetBoolean().Should().BeFalse();
    }

    // Feature #6912. A page that cannot read the model cannot know what the ground is shaped from, so
    // whatever the model says about it travels with the source or the page draws flat for everybody.
    [Fact]
    public async Task It_answers_what_a_source_says_about_raising_the_ground()
    {
        await using var factory = AnsweringWith(DeclaredModel.Seeded().Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        var source = answered.GetProperty("basemapSources").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == WillowBend.VectorBasemapName);
        source.GetProperty("terrainTileUrl").GetString().Should().Be(WillowBend.TerrainTileUrl);
        source.GetProperty("terrainEncoding").GetString().Should().Be(WillowBend.TerrainEncoding);
        source.GetProperty("terrainExaggeration").GetDouble().Should().Be(WillowBend.TerrainExaggeration);
        source.GetProperty("buildingSourceLayer").GetString().Should().Be(WillowBend.BuildingSourceLayer);
    }

    [Fact]
    public async Task A_source_saying_nothing_about_the_ground_answers_nothing_about_it()
    {
        var model = DeclaredModel.Seeded()
            .Stating(
                WillowBend.VectorBasemapName,
                ("attribution", WillowBend.BasemapAttribution),
                ("styleUrl", WillowBend.VectorBasemapStyleUrl));
        await using var factory = AnsweringWith(model.Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        var source = answered.GetProperty("basemapSources").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == WillowBend.VectorBasemapName);
        source.GetProperty("terrainTileUrl").ValueKind.Should().Be(JsonValueKind.Null);
        source.GetProperty("buildingSourceLayer").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task It_counts_against_the_callers_budget()
    {
        await using var factory = AnsweringWith(DeclaredModel.Seeded().Build());
        using var client = factory.CreateClient();

        HttpResponseMessage? refused = null;
        for (var attempt = 0; attempt <= SubmissionRate.RequestsAllowed; attempt++)
            refused = await client.GetAsync("/submissions/form");

        refused!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // A model nobody seeded is a fault in the deployment rather than in the request, and this route
    // answers a stranger's browser, which is told neither what is wrong nor that anything is.
    [Fact]
    public async Task A_model_that_was_never_seeded_is_not_described_to_the_caller()
    {
        await using var factory = AnsweringWith(new DeclaredModel().Build());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/submissions/form");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should()
            .NotContain(DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);
        factory.Log.Lines.Should().Contain(line =>
            line.Contains(DeclaredVocabularyReader.AllocationCategoryArchetypeFlag));
    }
}
