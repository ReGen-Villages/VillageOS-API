using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

// Reading the two vocabularies out of a model that declares them. Nothing here names an archetype or a
// predicate: each is found by the mark it carries, so a model that renamed either keeps answering — which
// is what a producer needs before it can write an edge to a term.
public class DeclaredVocabularyReaderTests
{
    [Fact]
    public void The_terms_are_the_things_declared_under_the_marked_archetype()
    {
        var model = DeclaredModel.Seeded();

        var vocabulary = DeclaredVocabularyReader.Read(model.Build());

        vocabulary.AllocationCategories.Terms.Select(term => term.Name).Should()
            .BeEquivalentTo(WillowBend.AllocationCategoryNames);
        vocabulary.BoundarySources.Terms.Select(term => term.Name).Should()
            .BeEquivalentTo(WillowBend.BoundarySourceNames);
    }

    // A refusal names the terms in this order. Left as the snapshot happened to carry them, one unknown word
    // would be refused in different words on different reads, and a planner comparing two attempts would be
    // reading a difference that says nothing.
    [Fact]
    public void The_terms_come_back_in_the_order_a_refusal_names_them_in()
    {
        var vocabulary = DeclaredVocabularyReader.Read(DeclaredModel.Seeded().Build());

        vocabulary.AllocationCategories.Terms.Select(term => term.Name).Should()
            .Equal([.. WillowBend.AllocationCategoryNames.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void Each_vocabulary_carries_the_predicate_its_edges_are_written_with()
    {
        var model = DeclaredModel.Seeded();

        var vocabulary = DeclaredVocabularyReader.Read(model.Build());

        vocabulary.AllocationCategories.Predicate.Id.Should().Be(model.Id("categorizedAs"));
        vocabulary.BoundarySources.Predicate.Id.Should().Be(model.Id("obtainedBy"));
    }

    // The archetype is not one of its own terms. Left in, it would resolve as a category a planner could
    // submit, and an allocation would be categorised as the idea of a category.
    [Fact]
    public void The_archetype_is_not_among_the_terms_declared_under_it()
    {
        var vocabulary = DeclaredVocabularyReader.Read(DeclaredModel.Seeded().Build());

        vocabulary.AllocationCategories.Terms.Should().NotContain(term => term.Name == "AllocationCategory");
        vocabulary.BoundarySources.Terms.Should().NotContain(term => term.Name == "BoundarySource");
    }

    // A model may group terms under a sub-type. The mark is on the archetype at the top, so the walk has to
    // reach a term through the intermediate type rather than stopping at it.
    [Fact]
    public void A_term_reached_through_an_intermediate_type_is_a_term()
    {
        var model = DeclaredModel.Seeded()
            .WithArchetype("PerennialCategory")
            .Relate("PerennialCategory", "is", "AllocationCategory")
            .Relate("silvopasture", "is", "PerennialCategory");

        var vocabulary = DeclaredVocabularyReader.Read(model.Build());

        vocabulary.AllocationCategories.Terms.Select(term => term.Name).Should().Contain("silvopasture")
            .And.NotContain("PerennialCategory");
    }

    // The two halves fail differently and both have to be named. A model with no marked archetype declares
    // no vocabulary at all; one with no marked predicate declares the terms and nothing to reach them with.
    [Theory]
    [InlineData("AllocationCategory")]
    [InlineData("categorizedAs")]
    [InlineData("BoundarySource")]
    [InlineData("obtainedBy")]
    public void A_model_missing_either_half_of_a_vocabulary_is_refused(string absent)
    {
        var model = DeclaredModel.Seeded().Without(absent);

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("Seed the model from the analysis templates");
    }

    // Two carriers leave nothing able to say which vocabulary a submitted word belongs to, and picking
    // either would resolve one word two ways on two submissions.
    [Fact]
    public void A_model_marking_two_archetypes_for_one_vocabulary_is_refused()
    {
        var model = DeclaredModel.Seeded()
            .WithArchetype("ProgrammeCategory", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("AllocationCategory").And.Contain("ProgrammeCategory");
    }

    // A mark on something nothing can `is` declares an empty vocabulary, and every submission would then be
    // refused for naming a term that is sitting in the model.
    [Fact]
    public void A_mark_carried_by_something_that_is_not_a_type_is_refused()
    {
        var model = DeclaredModel.Seeded()
            .Without("AllocationCategory")
            .With("AllocationCategory", DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("not a type");
    }

    // The selector asks for both vocabularies by mark and for nothing by name but `is`, which the walk from
    // an archetype to its terms has to recognise among the edges the snapshot carries.
    [Fact]
    public void The_read_asks_for_the_vocabularies_by_mark_rather_than_by_name()
    {
        var selector = DeclaredVocabularyReader.Selector();

        selector.MarkedTypes.Should().BeEquivalentTo(
            DeclaredVocabularyReader.AllocationCategoryArchetypeFlag,
            DeclaredVocabularyReader.BoundarySourceArchetypeFlag);
        selector.MarkedArchetypes.Should().BeEquivalentTo(
            DeclaredVocabularyReader.AllocationCategoryPredicateFlag,
            DeclaredVocabularyReader.BoundarySourcePredicateFlag);
        selector.Names.Should().Equal(SubmissionFragmentComposer.IsPredicateName);
        selector.IncludeRelationships.Should().BeTrue();
    }
}
