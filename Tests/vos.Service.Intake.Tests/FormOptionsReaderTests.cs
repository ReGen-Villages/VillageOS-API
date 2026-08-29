using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

// What a page holding no credential is told about the model it is submitting into. The categories are
// found by the mark their archetype carries, so a model that renamed the archetype keeps answering; the
// basemap sources are found by the archetype name every client that draws a map already reads.
public class FormOptionsReaderTests
{
    [Fact]
    public void The_categories_are_the_terms_declared_under_the_marked_archetype()
    {
        var options = FormOptionsReader.Read(DeclaredModel.Seeded().Build());

        options.AllocationCategories.Should().BeEquivalentTo(WillowBend.AllocationCategoryNames);
    }

    [Fact]
    public void A_model_that_calls_the_archetype_something_else_still_answers()
    {
        var model = new DeclaredModel()
            .WithArchetype("LandUse", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag)
            .With("putTo", DeclaredVocabularyReader.AllocationCategoryPredicateFlag)
            .Relate("Orchard", "is", "LandUse");

        var options = FormOptionsReader.Read(model.Build());

        options.AllocationCategories.Should().Equal("Orchard");
    }

    // A category the form offers and a submission is then refused for naming would be worse than no form,
    // and a model missing the predicate refuses every submission that names a category.
    [Fact]
    public void A_model_declaring_the_categories_but_not_the_predicate_is_refused_as_unseeded()
    {
        var model = new DeclaredModel()
            .WithArchetype("AllocationCategory", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag)
            .Relate("Orchard", "is", "AllocationCategory");

        var reading = () => FormOptionsReader.Read(model.Build());

        reading.Should().Throw<ModelNotSeededError>()
            .WithMessage("*" + DeclaredVocabularyReader.AllocationCategoryPredicateFlag + "*");
    }

    [Fact]
    public void A_basemap_source_travels_as_the_model_states_it()
    {
        var options = FormOptionsReader.Read(DeclaredModel.Seeded().Build());

        var streets = options.BasemapSources.Single(source => source.Name == WillowBend.VectorBasemapName);
        streets.StyleUrl.Should().Be(WillowBend.VectorBasemapStyleUrl);
        streets.Attribution.Should().Be(WillowBend.BasemapAttribution);
        streets.TileUrl.Should().BeNull();
    }

    // The deepest zoom a raster pyramid has tiles for is the model's to state and the map's to fall back
    // on. Answering a figure of this service's own would be a second place the fallback lived.
    [Fact]
    public void A_raster_source_carries_the_deepest_zoom_the_model_states_and_no_figure_it_does_not()
    {
        var model = DeclaredModel.Seeded()
            .Stating("Aerial", ("attribution", "Orthophoto"), ("tileUrl", "https://tiles.example.test/{z}/{x}/{y}.png"))
            .Relate("Aerial", "is", FormOptionsReader.BasemapSourceArchetypeName);

        var options = FormOptionsReader.Read(model.Build());

        var aerial = options.BasemapSources.Single(source => source.Name == "Aerial");
        aerial.TileUrl.Should().Be("https://tiles.example.test/{z}/{x}/{y}.png");
        aerial.MaximumZoom.Should().BeNull();
    }

    // A deployment whose model declares no imagery draws no map, which is a form with one step less rather
    // than a service that cannot answer.
    [Fact]
    public void A_model_declaring_no_basemap_answers_with_none_rather_than_refusing()
    {
        var model = new DeclaredModel()
            .WithArchetype("AllocationCategory", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag)
            .With("categorizedAs", DeclaredVocabularyReader.AllocationCategoryPredicateFlag)
            .Relate("Orchard", "is", "AllocationCategory");

        var options = FormOptionsReader.Read(model.Build());

        options.BasemapSources.Should().BeEmpty();
    }

    // The route is anonymous, so what it answers is what anybody may read. The other vocabularies the
    // service resolves a submission against are none of a form's business, and neither is anything else
    // the model happens to hold.
    [Fact]
    public void Nothing_the_form_does_not_draw_itself_with_travels()
    {
        var answered = JsonSerializer.Serialize(FormOptionsReader.Read(DeclaredModel.Seeded().Build()));

        foreach (var withheld in WillowBend.HazardTypeNames.Concat(WillowBend.BoundarySourceNames))
            answered.Should().NotContain(withheld);
    }

    [Fact]
    public void An_unseeded_model_is_refused_rather_than_answered_empty()
    {
        var reading = () => FormOptionsReader.Read(new DeclaredModel().Build());

        reading.Should().Throw<ModelNotSeededError>();
    }
}
