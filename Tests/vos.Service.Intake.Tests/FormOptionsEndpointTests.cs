using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

// The route a public form draws itself from. It demands no credential, for the same reason the submission
// route demands none: the page asking is a stranger's browser, and there is nothing for it to hold.
public class FormOptionsEndpointTests
{
    private static readonly JsonSerializerOptions AsTheBrokerWritesIt = new(JsonSerializerDefaults.Web);

    private static IntakeWebApplicationFactory AnsweringWith(SnapshotDocument model) => new()
    {
        HandlerCallback = _ => ModelStub.Json(JsonSerializer.Serialize(new SnapshotReadResult(0, model), AsTheBrokerWritesIt)),
    };

    // A broker that counts what it is asked. A declaration read is a POST of a selector, whichever
    // route it takes; a token exchange is answered with whatever token the test says the service holds now.
    private sealed class CountingBroker(SnapshotDocument model)
    {
        public SnapshotDocument Model { get; set; } = model;
        public HttpStatusCode Answers { get; set; } = HttpStatusCode.OK;
        public Func<string>? TokenMinted { get; set; }
        public List<HttpRequestMessage> Requests { get; } = [];

        public int DeclarationReads => Requests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath is "/api/snapshots" or "/api/subscriptions");

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Requests.Add(request);
            if (request.RequestUri!.AbsolutePath == "/api/auth/token")
                return ModelStub.Json(JsonSerializer.Serialize(new { token = TokenMinted!() }));
            if (Answers != HttpStatusCode.OK) return new HttpResponseMessage(Answers);
            return ModelStub.Json(JsonSerializer.Serialize(new SnapshotReadResult(0, Model), AsTheBrokerWritesIt));
        }
    }

    private static IntakeWebApplicationFactory Counting(CountingBroker broker) => new() { HandlerCallback = broker.Answer };

    private static async Task<JsonElement> FormAsync(HttpClient client)
    {
        var response = await client.GetAsync("/submissions/form");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string?> Categories(JsonElement form) =>
        form.GetProperty("allocationCategories").EnumerateArray().Select(category => category.GetString());

    // Every public request asks the model the same question, and the answer changes only when the model does.
    [Fact]
    public async Task Two_form_requests_make_one_declaration_read()
    {
        var broker = new CountingBroker(DeclaredModel.Seeded().Build());
        await using var factory = Counting(broker);
        using var client = factory.CreateClient();

        await FormAsync(client);
        await FormAsync(client);

        broker.DeclarationReads.Should().Be(1);
    }

    [Fact]
    public async Task Once_the_hold_has_run_a_changed_model_is_read_again()
    {
        var broker = new CountingBroker(DeclaredModel.Seeded().Build());
        await using var factory = Counting(broker);
        using var client = factory.CreateClient();
        Categories(await FormAsync(client)).Should().NotContain("Vineyard");

        broker.Model = DeclaredModel.Seeded().Relate("Vineyard", "is", "AllocationCategory").Build();
        factory.Clock.Advance(HeldDeclarations.HeldFor);
        var form = await FormAsync(client);

        Categories(form).Should().Contain("Vineyard");
        broker.DeclarationReads.Should().Be(2);
    }

    // A held declaration belongs to one model. The service's token names the model the broker binds its
    // calls to, so a token naming another model reads afresh whatever the clock says.
    [Fact]
    public async Task A_token_naming_another_model_reads_again_within_the_hold()
    {
        var modelNamed = Guid.NewGuid();
        // Expiring too soon to be held, so every call exchanges the key and gets the token the test mints now.
        var broker = new CountingBroker(DeclaredModel.Seeded().Build())
        {
            TokenMinted = () => TestTokens.For(modelNamed, DateTimeOffset.UtcNow.AddSeconds(20)),
        };
        await using var factory = new IntakeWebApplicationFactory
        {
            Token = null, ApiKey = "vos_sk_a-key-the-broker-would-accept", HandlerCallback = broker.Answer,
        };
        using var client = factory.CreateClient();
        await FormAsync(client);

        modelNamed = Guid.NewGuid();
        await FormAsync(client);

        broker.DeclarationReads.Should().Be(2);
    }

    [Fact]
    public async Task A_read_that_failed_is_not_held()
    {
        var broker = new CountingBroker(DeclaredModel.Seeded().Build()) { Answers = HttpStatusCode.InternalServerError };
        await using var factory = Counting(broker);
        using var client = factory.CreateClient();
        (await client.GetAsync("/submissions/form")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        broker.Answers = HttpStatusCode.OK;
        var response = await client.GetAsync("/submissions/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        broker.DeclarationReads.Should().Be(2);
    }

    // A subscription is checked by the broker on every later write until it is deleted; a read made once
    // goes through the route that registers nothing.
    [Fact]
    public async Task A_declaration_read_registers_nothing_on_the_broker()
    {
        var broker = new CountingBroker(DeclaredModel.Seeded().Build());
        await using var factory = Counting(broker);
        using var client = factory.CreateClient();

        await FormAsync(client);

        broker.Requests.Select(request => request.RequestUri!.AbsolutePath).Should()
            .NotContain(path => path.StartsWith("/api/subscriptions", StringComparison.Ordinal));
        broker.Requests.Should().Contain(request =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/snapshots");
    }

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

    // The words travel under the name the list offers, exactly as the model spells it: a key recased on the
    // way out would match no term, and every term would be shown by its bare name.
    [Fact]
    public async Task It_answers_each_terms_wording_under_the_name_as_the_model_spells_it()
    {
        var model = new DeclaredModel()
            .WithArchetype("LandUse").Bound(LandIntakeRoles.AllocationCategory, "LandUse")
            .With("putTo").Bound(LandIntakeRoles.CategorizedAs, "putTo")
            .Relate("Orchard", "is", "LandUse")
            .Stating("Orchard", (FormOptionsReader.WordingProperty, """{"en": "Orchard", "fr": "Verger"}"""));
        await using var factory = AnsweringWith(model.Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        answered.GetProperty("wording").GetProperty("Orchard").GetProperty("fr").GetString().Should().Be("Verger");
    }

    [Fact]
    public async Task It_answers_the_themes_a_report_may_name()
    {
        var model = DeclaredModel.Seeded()
            .WithArchetype("Theme").Bound(FormOptionsReader.ThemeRole, "Theme")
            .Stating("Water", ("colour", "#3B7DE0"), ("icon", "droplet"), ("order", 2))
            .Relate("Water", "is", "Theme");
        await using var factory = AnsweringWith(model.Build());
        using var client = factory.CreateClient();

        var answered = await client.GetFromJsonAsync<JsonElement>("/submissions/form");

        var water = answered.GetProperty("themes").EnumerateArray().Single();
        water.GetProperty("name").GetString().Should().Be("Water");
        water.GetProperty("colour").GetString().Should().Be("#3B7DE0");
        water.GetProperty("icon").GetString().Should().Be("droplet");
        water.GetProperty("order").GetInt64().Should().Be(2);
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
                share.GetProperty("sharePercent").GetDouble()))
            .Should().BeEquivalentTo(
                WillowBend.DefaultProgramme.Select(share => (share.Category, share.SharePercent)));
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

    // A page that cannot read the model cannot know what the ground is shaped from, so
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
        var (code, values) = await RefusalReading.ReadAsync(refused);
        code.Should().Be(RefusalCode.TooManyRequests);
        values.GetProperty("seconds").GetInt32().Should().BePositive();
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
            .NotContain(LandIntakeRoles.AllocationCategory.Word);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.ServiceUnavailable);
        factory.Log.Lines.Should().Contain(line =>
            line.Contains(LandIntakeRoles.AllocationCategory.Word));
    }
}
