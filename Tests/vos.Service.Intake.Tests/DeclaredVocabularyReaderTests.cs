using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

// Reading the vocabularies out of a model that declares them. Nothing here names an archetype or a
// predicate: each is found through the role Thing that binds it, so a model that renamed either keeps
// answering — which is what a producer needs before it can write a relationship to a term.
public class DeclaredVocabularyReaderTests
{
    [Fact]
    public void The_terms_are_the_things_declared_under_the_archetype_the_role_binds()
    {
        var model = DeclaredModel.Seeded();

        var vocabulary = DeclaredVocabularyReader.Read(model.Build());

        vocabulary.AllocationCategories.Terms.Select(term => term.Name).Should()
            .BeEquivalentTo(WillowBend.AllocationCategoryNames);
        vocabulary.BoundarySources.Terms.Select(term => term.Name).Should()
            .BeEquivalentTo(WillowBend.BoundarySourceNames);
        vocabulary.HazardTypes.Terms.Select(term => term.Name).Should()
            .BeEquivalentTo(WillowBend.HazardTypeNames);
    }

    // The roles are what the reader asks for, so a model calling every archetype and predicate something
    // else still answers with its terms. Every name here is spelled unlike the shipped template.
    [Fact]
    public void A_model_binding_the_roles_to_things_of_other_names_is_read()
    {
        var model = new DeclaredModel()
            .WithArchetype("LandUse").Bound(LandIntakeRoles.AllocationCategory, "LandUse")
            .With("usedFor").Bound(LandIntakeRoles.CategorizedAs, "usedFor")
            .WithArchetype("Provenance").Bound(LandIntakeRoles.BoundarySource, "Provenance")
            .With("tracedBy").Bound(LandIntakeRoles.ObtainedBy, "tracedBy")
            .WithArchetype("Peril").Bound(LandIntakeRoles.HazardType, "Peril")
            .With("concerns").Bound(LandIntakeRoles.Assesses, "concerns")
            .WithArchetype("Severity").Bound(LandIntakeRoles.HazardLevel, "Severity")
            .With("gradedAs").Bound(LandIntakeRoles.ReportedLevel, "gradedAs")
            .With("World").Bound(LandIntakeRoles.RootPlace, "World")
            .With("within").Bound(LandIntakeRoles.PlaceNesting, "within")
            .WithArchetype("Appraisal").Bound(LandIntakeRoles.SiteStudy, "Appraisal")
            .Relate("housing", "is", "LandUse")
            .Relate("walked", "is", "Provenance")
            .Relate("flooding", "is", "Peril")
            .Relate("bad", "is", "Severity");

        var vocabulary = DeclaredVocabularyReader.Read(model.Build());

        vocabulary.AllocationCategories.Terms.Select(term => term.Name).Should().Equal("housing");
        vocabulary.AllocationCategories.Predicate.Name.Should().Be("usedFor");
        vocabulary.BoundarySources.Terms.Select(term => term.Name).Should().Equal("walked");
        vocabulary.HazardTypes.Predicate.Name.Should().Be("concerns");
        vocabulary.HazardLevels.Terms.Select(term => term.Name).Should().Equal("bad");
        vocabulary.PlaceNesting.Root.Name.Should().Be("World");
        vocabulary.PlaceNesting.Predicate.Name.Should().Be("within");
        vocabulary.SiteStudy.Name.Should().Be("Appraisal");
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
        vocabulary.HazardTypes.Predicate.Id.Should().Be(model.Id("assesses"));
    }

    [Fact]
    public void The_study_archetype_is_the_one_the_role_binds()
    {
        var model = DeclaredModel.Seeded();

        DeclaredVocabularyReader.Read(model.Build()).SiteStudy.Id.Should().Be(model.Id("SiteStudy"));
    }

    // The archetype is not one of its own terms. Left in, it would resolve as a category a planner could
    // submit, and an allocation would be categorised as the idea of a category.
    [Fact]
    public void The_archetype_is_not_among_the_terms_declared_under_it()
    {
        var vocabulary = DeclaredVocabularyReader.Read(DeclaredModel.Seeded().Build());

        vocabulary.AllocationCategories.Terms.Should().NotContain(term => term.Name == "AllocationCategory");
        vocabulary.BoundarySources.Terms.Should().NotContain(term => term.Name == "BoundarySource");
        vocabulary.HazardTypes.Terms.Should().NotContain(term => term.Name == "HazardType");
    }

    // A model may group terms under a sub-type. The role binds the archetype at the top, so the walk has to
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

    // The two halves fail differently and both have to be named. A model binding no archetype declares
    // no vocabulary at all; one binding no predicate declares the terms and nothing to reach them with.
    [Theory]
    [InlineData("AllocationCategory")]
    [InlineData("categorizedAs")]
    [InlineData("BoundarySource")]
    [InlineData("obtainedBy")]
    [InlineData("HazardType")]
    [InlineData("assesses")]
    [InlineData("SiteStudy")]
    public void A_model_missing_either_half_of_a_vocabulary_is_refused(string absent)
    {
        var model = DeclaredModel.Seeded().Without(absent);

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("Seed the model from the analysis templates");
    }

    // Two bindings leave nothing able to say which vocabulary a submitted word belongs to, and picking
    // either would resolve one word two ways on two submissions.
    [Fact]
    public void A_model_binding_two_archetypes_to_one_vocabulary_role_is_refused()
    {
        var model = DeclaredModel.Seeded()
            .WithArchetype("ProgrammeCategory").Bound(LandIntakeRoles.AllocationCategory, "ProgrammeCategory");

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("AllocationCategory").And.Contain("ProgrammeCategory");
    }

    // A role bound to something nothing can `is` declares an empty vocabulary, and every submission would
    // then be refused for naming a term that is sitting in the model.
    [Fact]
    public void A_role_bound_to_something_that_is_not_a_type_is_refused()
    {
        var model = DeclaredModel.Seeded()
            .Without("AllocationCategory")
            .With("AllocationCategory");

        var refusal = Assert.Throws<ModelNotSeededError>(() => DeclaredVocabularyReader.Read(model.Build()));

        refusal.Message.Should().Contain("not a type");
    }

    // The selector asks for the role Things by identifier and follows their bindings, so the snapshot says
    // which bound Thing plays which role; the term archetypes come with their members; and `is` and `binds`
    // are the only names, which the walks have to recognise among the relationships the snapshot carries.
    [Fact]
    public void The_read_asks_for_the_roles_and_their_bindings_rather_than_for_names()
    {
        var selector = DeclaredVocabularyReader.Selector();

        selector.Ids.Should().BeEquivalentTo(RoleBindings.Identifiers(
            LandIntakeRoles.AllocationCategory, LandIntakeRoles.BoundarySource, LandIntakeRoles.HazardType,
            LandIntakeRoles.HazardLevel, LandIntakeRoles.WaterDemandComponent, LandIntakeRoles.CategorizedAs,
            LandIntakeRoles.ObtainedBy, LandIntakeRoles.Assesses, LandIntakeRoles.ReportedLevel,
            LandIntakeRoles.SiteStudy, LandIntakeRoles.RootPlace, LandIntakeRoles.PlaceNesting));
        selector.BoundTypes.Should().BeEquivalentTo(RoleBindings.Identifiers(
            LandIntakeRoles.AllocationCategory, LandIntakeRoles.BoundarySource, LandIntakeRoles.HazardType,
            LandIntakeRoles.HazardLevel, LandIntakeRoles.WaterDemandComponent));
        selector.Traverse.Should().Equal(RoleBindings.FollowBindings);
        selector.Names.Should().BeEquivalentTo(
            SubmissionFragmentComposer.IsPredicateName, RoleBindings.BindsPredicateName);
        selector.IncludeRelationships.Should().BeTrue();
    }

    // The demands one harvest is served over, which are archetypes rather than terms: a study holds a
    // demand of its own under each, and nothing in a submission names one. Read in serving order, because
    // the producer writes the ordering as relationships and would otherwise write whatever order the snapshot
    // happened to carry.
    [Fact]
    public void The_water_demands_come_back_in_the_order_the_analysis_serves_them()
    {
        var demands = DeclaredVocabularyReader.Read(DeclaredModel.Seeded().Build()).WaterDemands;

        demands.Select(demand => demand.Name).Should().Equal("domestic-demand", "irrigation-demand");
        demands.Select(demand => demand.ServingOrder).Should().Equal(1, 2);
    }

    // A model doing no water analysis declares no demand vocabulary, and a study then holds no demand and
    // reads unassessed on every coverage. Unlike a submitted word naming a term nothing holds — there the
    // word is already written and something has to say it means nothing.
    [Fact]
    public void A_model_declaring_no_water_demands_reads_none_rather_than_refusing()
    {
        var model = DeclaredModel.Seeded().Without("WaterDemandComponent");

        DeclaredVocabularyReader.Read(model.Build()).WaterDemands.Should().BeEmpty();
    }

    // An analysis stating no order at all still composes: every demand reads nought and is served in the
    // order the model declared it, which the producer then writes as the ordering relationships.
    [Fact]
    public void A_demand_with_no_stated_order_reads_as_served_first()
    {
        var model = DeclaredModel.Seeded();
        foreach (var (demand, _) in WillowBend.WaterDemandNames)
            model.ArchetypeStating(demand).Relate(demand, "is", "WaterDemandComponent");

        DeclaredVocabularyReader.Read(model.Build()).WaterDemands
            .Select(demand => demand.ServingOrder).Should().AllBeEquivalentTo(0L);
    }

    // The root Place a site is related to, and the predicate that relationship is written with. Both are
    // found through a role rather than by name for the same reason every other vocabulary is: a producer
    // naming `Earth` relates nothing, and says nothing, in a model that calls its root something else — and
    // a site related to no Place is one no source is ever selected for.
    [Fact]
    public void The_root_place_and_its_nesting_predicate_are_found_through_their_roles()
    {
        var vocabulary = DeclaredVocabularyReader.Read(DeclaredModel.Seeded().Build());

        vocabulary.PlaceNesting.Root.Name.Should().Be("Earth");
        vocabulary.PlaceNesting.Predicate.Name.Should().Be("isIn");
    }

    [Fact]
    public void A_model_declaring_no_root_place_is_refused_rather_than_leaving_the_site_unrelated()
    {
        var model = DeclaredModel.Seeded().Without("Earth");

        var refusal = () => DeclaredVocabularyReader.Read(model.Build());

        refusal.Should().Throw<ModelNotSeededError>().WithMessage("*rootPlace*");
    }

    // Two roots leave nothing able to say which one a site sits under, and a site would land beneath a
    // different one on different submissions — so the sources selected for it would differ by run.
    [Fact]
    public void A_model_binding_two_root_places_is_refused_rather_than_picked_between()
    {
        var model = DeclaredModel.Seeded().With("Mars").Bound(LandIntakeRoles.RootPlace, "Mars");

        var refusal = () => DeclaredVocabularyReader.Read(model.Build());

        refusal.Should().Throw<ModelNotSeededError>().WithMessage("*more than one*");
    }

    [Fact]
    public void A_model_declaring_no_nesting_predicate_is_refused()
    {
        var model = DeclaredModel.Seeded().Unbound(LandIntakeRoles.PlaceNesting);

        var refusal = () => DeclaredVocabularyReader.Read(model.Build());

        refusal.Should().Throw<ModelNotSeededError>().WithMessage("*placeNesting*");
    }
}
