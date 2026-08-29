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
