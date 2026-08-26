using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>
/// What a submission has to put into the model. The tests about the study, the site and the parcel are the
/// executable form of clauses the platform's own fixture pins, so a site built here and a site built by an
/// imported building model stay the same shape and one reader serves both.
/// </summary>
public class SubmissionFragmentComposerTests
{
    private static ComposedSubmission Compose(Submission submission) =>
        Compose(submission, WillowBend.KnownVocabulary);

    private static ComposedSubmission Compose(Submission submission, DeclaredVocabulary vocabulary) =>
        SubmissionFragmentComposer.Compose(
            submission, WillowBend.KnownPredicates, WillowBend.KnownArchetypes, vocabulary, WillowBend.ArrivedAt);

    private static ComposedSubmission Compose(Submission submission, DateTime? arrivedAt) =>
        SubmissionFragmentComposer.Compose(
            submission, WillowBend.KnownPredicates, WillowBend.KnownArchetypes, WillowBend.KnownVocabulary,
            arrivedAt);

    private static FragmentThing Record(ComposedSubmission composed) => Named(composed, "Willow Bend Submission");

    private static bool Relates(ComposedSubmission composed, Guid subject, Guid predicate, Guid target) =>
        composed.Fragment.Relationships.Any(edge =>
            edge.Subject == subject && edge.Predicate == predicate && edge.Target == target);

    private static bool IsEdgeTo(ComposedSubmission composed, Guid subject, Guid archetype) =>
        Relates(composed, subject, WillowBend.IsPredicateId, archetype);

    private static FragmentThing Thing(ComposedSubmission composed, Guid id) =>
        composed.Fragment.Things.Single(thing => thing.Id == id);

    private static FragmentThing Named(ComposedSubmission composed, string name) =>
        composed.Fragment.Things.Single(thing => thing.Name == name);

    private static bool Holds(ComposedSubmission composed, Guid subject, Guid target) =>
        Relates(composed, subject, WillowBend.HasPredicateId, target);

    // The project holds the site, not the reverse: a submission is one planner's undertaking and the site is
    // what it is about. A reader walking a project's parts finds the site among them.
    [Fact]
    public void The_project_holds_the_site_the_submission_is_about()
    {
        var composed = Compose(WillowBend.Submission());

        var project = Named(composed, "Willow Bend Regeneration");
        Holds(composed, project.Id, composed.SiteId).Should().BeTrue();
        IsEdgeTo(composed, project.Id, WillowBend.ProjectArchetypeId).Should().BeTrue();
    }

    // Who to ask hangs off what is being asked about, so a contact can change without rewriting the project.
    [Fact]
    public void The_contact_hangs_off_the_project_rather_than_the_site()
    {
        var composed = Compose(WillowBend.Submission());

        var project = Named(composed, "Willow Bend Regeneration");
        var contact = Named(composed, "Ana Ferreira");
        Holds(composed, project.Id, contact.Id).Should().BeTrue();
        Holds(composed, composed.SiteId, contact.Id).Should().BeFalse(
            "a contact is reached through the project it can be asked about");
        IsEdgeTo(composed, contact.Id, WillowBend.ContactArchetypeId).Should().BeTrue();
    }

    [Fact]
    public void A_project_writes_the_properties_its_archetype_declares()
    {
        var composed = Compose(WillowBend.Submission());

        var project = Named(composed, "Willow Bend Regeneration");
        project.Properties.Should().ContainKeys("country", "nearestCity", "existingDataNotes");
        Named(composed, "Ana Ferreira").Properties.Should()
            .ContainKeys("relationshipToProject", "emailAddress", "phoneNumber");
    }

    // Every field beyond the submission identifier and the site name is optional, because a wizard saves as
    // the planner fills it in.
    [Fact]
    public void A_submission_naming_no_project_mints_neither_it_nor_a_contact()
    {
        var composed = Compose(WillowBend.Submission() with { Project = null, Contact = null });

        composed.Fragment.Things.Should().OnlyContain(thing => thing.Name != "Willow Bend Regeneration");
        composed.Fragment.Things.Select(thing => thing.Name).Should().NotContain("Ana Ferreira");
    }

    // A contact has nowhere to hang without a project, and a fragment that silently dropped it would leave
    // the planner believing it was recorded.
    [Fact]
    public void A_contact_given_without_a_project_is_refused()
    {
        var refusal = Assert.Throws<SubmissionError>(() =>
            Compose(WillowBend.Submission() with { Project = null }));

        refusal.Message.Should().Contain("contact").And.Contain("project");
    }

    private static IEnumerable<FragmentThing> Allocations(ComposedSubmission composed) =>
        composed.Fragment.Things.Where(thing =>
            IsEdgeTo(composed, thing.Id, WillowBend.ProgrammeAllocationArchetypeId));

    // Which category a share is for is asked of the edge, because that is the only place it is recorded.
    private static FragmentThing AllocationFor(ComposedSubmission composed, string categoryName) =>
        Allocations(composed).Single(thing => Relates(
            composed, thing.Id, WillowBend.CategorizedAsPredicateId, WillowBend.TermId(categoryName)));

    [Fact]
    public void Each_allocation_is_a_thing_of_its_own_the_site_holds()
    {
        var composed = Compose(WillowBend.Submission());

        // The count is what pins the archetype edge: an allocation is found by it and by nothing else,
        // so one share short of the submitted set means one share reached no archetype.
        var allocations = Allocations(composed).ToList();
        allocations.Should().HaveCount(WillowBend.Submission().Allocations!.Count);
        allocations.Should().OnlyContain(thing =>
            composed.Fragment.Relationships.Any(edge =>
                edge.Subject == composed.SiteId
                && edge.Predicate == WillowBend.HasPredicateId
                && edge.Target == thing.Id));
    }

    // What land allocation reads to work out which footprint a share belongs to. Without this edge every
    // allocation reads as uncategorised and the analysis computes nothing, which is what a word beside it
    // could never fix: a word cannot be walked to and carries no flags.
    [Fact]
    public void Each_allocation_reaches_the_category_the_model_declares()
    {
        var composed = Compose(WillowBend.Submission());

        foreach (var name in WillowBend.AllocationCategoryNames)
            Allocations(composed)
                .Should().ContainSingle(thing =>
                    Relates(composed, thing.Id, WillowBend.CategorizedAsPredicateId, WillowBend.TermId(name)),
                    $"'{name}' has to be reached by exactly one share, through the category Thing the model declares");
    }

    [Fact]
    public void The_parcel_reaches_the_way_its_boundary_was_obtained()
    {
        var composed = Compose(WillowBend.Submission());

        Relates(composed, composed.ParcelId!.Value, WillowBend.ObtainedByPredicateId,
            WillowBend.TermId("drawn-by-hand")).Should().BeTrue();
    }

    // A term reached by an edge and copied into a property beside it says the same thing twice, and the
    // copy is the half no reader can walk from and no range can judge. Two readings of one value can also
    // come to disagree, which nothing would notice.
    [Theory]
    [InlineData("allocationCategory")]
    [InlineData("boundarySource")]
    public void A_term_the_fragment_reaches_by_an_edge_is_not_also_written_as_a_word(string word)
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Should().NotContain(thing => thing.Properties.ContainsKey(word));
    }

    // The predicate is the model's own, and the readers on the other side follow it by the mark it carries.
    // A predicate minted here would carry no mark, so the edge would be written and never followed — the
    // silent half of this failure rather than the loud one.
    [Fact]
    public void The_predicates_a_vocabulary_is_reached_through_are_never_minted_into_the_fragment()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Select(thing => thing.Id).Should()
            .NotContain(WillowBend.CategorizedAsPredicateId).And
            .NotContain(WillowBend.ObtainedByPredicateId);
    }

    // Two terms a submitted word cannot be told apart by is a model nothing can resolve against, and picking
    // either would send two submissions naming one word to two different Things.
    [Fact]
    public void A_model_declaring_two_terms_that_differ_only_in_case_is_refused()
    {
        var vocabulary = WillowBend.KnownVocabulary with
        {
            AllocationCategories = new DeclaredTerms(
                WillowBend.KnownVocabulary.AllocationCategories.Predicate,
                [
                    new DeclaredTerm("residential", WillowBend.TermId("residential")),
                    new DeclaredTerm("Residential", WillowBend.TermId("Residential")),
                ]),
        };

        var refusal = Assert.Throws<ModelNotSeededError>(() => Compose(
            WillowBend.Submission() with
            {
                Allocations = [new SubmittedAllocation { Category = "residential", SharePct = 100 }],
            },
            vocabulary));

        refusal.Message.Should().Contain("residential").And.Contain("Residential");
    }

    [Fact]
    public void An_allocation_writes_the_properties_its_archetype_declares()
    {
        var composed = Compose(WillowBend.Submission());

        AllocationFor(composed, "residential").Properties["sharePct"].Value.Should().Be(22.0);
    }

    // The shared analysis declares an allocation's area as a formula over its own share and the parcel its
    // site holds, and a derived property refuses every value write — so writing a submitted area would not
    // duplicate the answer, it would fail the whole fragment. An allocation carries its share and nothing
    // else this service could write (Bug #6762).
    [Fact]
    public void An_allocation_carries_no_figure_the_model_works_out_for_itself()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "residential", SharePct = 22 }],
        });

        AllocationFor(composed, "residential").Properties.Keys.Should().Equal("sharePct");
    }

    // Shares are normalised across the chosen categories further down the analysis, so a set that does not
    // reach a hundred is a wizard part-filled. Refusing it here would reject a submission mid-save.
    [Fact]
    public void Shares_that_do_not_add_to_a_hundred_are_taken_as_given()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "residential", SharePct = 12 }],
        });

        Allocations(composed).Single().Properties["sharePct"].Value.Should().Be(12.0);
    }

    // The vocabulary is the model's, so a project whose programme divides differently declares its own terms
    // and this service is not rebuilt. The category is unknown to every list anyone could have compiled in
    // here, and the model declaring it is the whole of what makes it resolve.
    [Fact]
    public void A_category_this_service_has_never_heard_of_resolves_when_the_model_declares_it()
    {
        var silvopasture = new DeclaredTerm("silvopasture", WillowBend.TermId("silvopasture"));
        var vocabulary = WillowBend.KnownVocabulary with
        {
            AllocationCategories = new DeclaredTerms(
                WillowBend.KnownVocabulary.AllocationCategories.Predicate, [silvopasture]),
        };

        var composed = Compose(
            WillowBend.Submission() with
            {
                Allocations = [new SubmittedAllocation { Category = "silvopasture", SharePct = 100 }],
            },
            vocabulary);

        var allocation = Allocations(composed).Single();
        Relates(composed, allocation.Id, WillowBend.CategorizedAsPredicateId, silvopasture.Id).Should().BeTrue();
        allocation.Name.Should().Be("Willow Bend silvopasture");
    }

    // A word no term matches is refused by naming what the model declares, so a planner is corrected by the
    // vocabulary the analysis will read rather than by a list this service was compiled with.
    [Fact]
    public void A_category_the_model_does_not_declare_is_refused_by_naming_what_it_does()
    {
        var refusal = Assert.Throws<SubmissionError>(() => Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "Silvopasture", SharePct = 100 }],
        }));

        refusal.Message.Should().Contain("Silvopasture").And.Contain("food-and-agriculture");
    }

    // A term is named as the model spells it, and case and surrounding space are not part of what a planner
    // meant by it.
    [Fact]
    public void A_category_typed_with_stray_case_and_space_resolves_to_the_term_the_model_declares()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "  Food-And-Agriculture ", SharePct = 100 }],
        });

        var allocation = Allocations(composed).Single();
        Relates(composed, allocation.Id, WillowBend.CategorizedAsPredicateId,
            WillowBend.TermId("food-and-agriculture")).Should().BeTrue();
        allocation.Name.Should().Be("Willow Bend food-and-agriculture");
    }

    // Two shares of one category disagree about it and nothing here can say which was meant. They are one
    // category because they resolve to one term, not because they were spelled alike.
    [Fact]
    public void Two_allocations_naming_one_category_are_refused()
    {
        var refusal = Assert.Throws<SubmissionError>(() => Compose(WillowBend.Submission() with
        {
            Allocations =
            [
                new SubmittedAllocation { Category = "Residential", SharePct = 22 },
                new SubmittedAllocation { Category = " residential ", SharePct = 30 },
            ],
        }));

        refusal.Message.Should().Contain("Residential").And.Contain("residential");
    }

    // Identity comes from the category, not from a position in the list, so a wizard that reorders them
    // re-posts onto the same Things rather than building a second set beside the first.
    [Fact]
    public void Reordering_the_allocations_lands_on_the_same_things()
    {
        var submission = WillowBend.Submission();
        var reversed = submission with { Allocations = [.. submission.Allocations!.Reverse()] };

        // Which identifier each category landed on, not merely the set of identifiers minted: a producer
        // deriving them from a position in the list mints the same set either way and hands them to
        // different categories, which a comparison of sets alone reads as unchanged.
        static Dictionary<string, Guid> ByCategory(ComposedSubmission composed) =>
            WillowBend.AllocationCategoryNames.ToDictionary(
                name => name, name => AllocationFor(composed, name).Id);

        ByCategory(Compose(reversed)).Should().BeEquivalentTo(ByCategory(Compose(submission)));
    }

    [Fact]
    public void A_submission_with_no_allocations_mints_none()
    {
        var composed = Compose(WillowBend.Submission() with { Allocations = null });

        Allocations(composed).Should().BeEmpty();
        composed.Fragment.Relationships.Should()
            .NotContain(edge => edge.Target == WillowBend.ProgrammeAllocationArchetypeId);
    }

    private static IEnumerable<FragmentThing> Hazards(ComposedSubmission composed) =>
        composed.Fragment.Things.Where(thing =>
            IsEdgeTo(composed, thing.Id, WillowBend.HazardAssessmentArchetypeId));

    private static FragmentThing HazardOf(ComposedSubmission composed, string hazardType) =>
        Hazards(composed).Single(thing =>
            Relates(composed, thing.Id, WillowBend.AssessesPredicateId, WillowBend.TermId(hazardType)));

    // What the assessment is about, as an edge to the Thing the model declares. A word could name a hazard
    // that exists nowhere and nothing would notice; nothing can be asked of it either — not what it means,
    // not which other sites carry it.
    [Fact]
    public void A_hazard_reaches_the_type_the_model_declares_and_carries_no_word_for_it()
    {
        var composed = Compose(WillowBend.Submission());

        var flood = HazardOf(composed, "river-flood");

        flood.Properties.Should().NotContainKey("hazardType",
            "a word beside the edge can be read but not walked from, and the two can come to disagree");
        Relates(composed, flood.Id, WillowBend.AssessesPredicateId, WillowBend.TermId("river-flood"))
            .Should().BeTrue();
    }

    // The predicate comes from the model, like the term. One minted here would carry no mark, so every
    // reader that follows this vocabulary by mark would miss the edge entirely.
    [Fact]
    public void The_edge_to_a_hazard_type_uses_the_predicate_the_model_declares()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Should().NotContain(thing => thing.Id == WillowBend.AssessesPredicateId,
            "a predicate the model already holds is used as it stands, never minted beside it");
    }

    [Fact]
    public void A_hazard_the_model_declares_no_type_for_is_refused_naming_what_it_does_declare()
    {
        var refusal = Assert.Throws<SubmissionError>(() => Compose(WillowBend.Submission() with
        {
            Hazards = [new SubmittedHazard { HazardType = "volcano" }],
        }));

        refusal.Message.Should().Contain("hazard.hazardType").And.Contain("volcano");
        foreach (var declared in WillowBend.HazardTypeNames)
            refusal.Message.Should().Contain(declared);
    }

    // A term added to the model reaches the next submission without this service being rebuilt, which is
    // the whole reason the types are Things.
    [Fact]
    public void A_type_the_model_adds_is_accepted_without_a_change_here()
    {
        var declared = WillowBend.KnownVocabulary;
        var withVolcano = declared with
        {
            HazardTypes = new DeclaredTerms(
                declared.HazardTypes.Predicate,
                [.. declared.HazardTypes.Terms, new DeclaredTerm("volcano", WillowBend.TermId("volcano"))]),
        };

        var composed = SubmissionFragmentComposer.Compose(
            WillowBend.Submission() with { Hazards = [new SubmittedHazard { HazardType = "volcano" }] },
            WillowBend.KnownPredicates, WillowBend.KnownArchetypes, withVolcano, WillowBend.ArrivedAt);

        Relates(composed, Hazards(composed).Single().Id, WillowBend.AssessesPredicateId,
            WillowBend.TermId("volcano")).Should().BeTrue();
    }

    // The whole point of the archetype change: a hazard says which source assessed it by hanging off that
    // source, so a reader can walk from one to the other. A name copied onto the hazard could be walked to
    // by nothing, and could disagree with the source's own with nothing to notice.
    [Fact]
    public void A_hazard_hangs_off_the_source_that_assessed_it()
    {
        var composed = Compose(WillowBend.Submission());

        var flood = HazardOf(composed, "river-flood");
        var source = Named(composed, "National flood portal");

        Holds(composed, flood.Id, source.Id).Should().BeTrue();
        IsEdgeTo(composed, source.Id, WillowBend.DataSourceArchetypeId).Should().BeTrue();
        flood.Properties.Should().NotContainKey("assessmentSource",
            "the source is a Thing this hangs off, not a name copied onto it");
    }

    // One portal read twice is one source. Minting it per hazard would leave two Things a reader cannot tell
    // apart, and discovery resolving one would leave the other stale.
    [Fact]
    public void Two_hazards_citing_one_source_share_that_source()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Where(thing => thing.Name == "National flood portal").Should().HaveCount(1);
        Hazards(composed).Should().OnlyContain(hazard =>
            Holds(composed, hazard.Id, Named(composed, "National flood portal").Id));
    }

    // A level a planner remembered is a recollection. It arrives as an observation when the source is
    // resolved, and a submission writing one would make an unassessed hazard read as assessed.
    [Fact]
    public void A_hazard_carries_no_level_and_no_date()
    {
        var composed = Compose(WillowBend.Submission());

        Hazards(composed).Should().OnlyContain(thing =>
            !thing.Properties.ContainsKey("hazardLevel") && !thing.Properties.ContainsKey("assessedOn"));
    }

    [Fact]
    public void Each_hazard_is_a_thing_of_its_own_the_site_holds()
    {
        var composed = Compose(WillowBend.Submission());

        var hazards = Hazards(composed).ToList();
        hazards.Should().HaveCount(WillowBend.Submission().Hazards!.Count);
        hazards.Should().OnlyContain(thing => Holds(composed, composed.SiteId, thing.Id));
        hazards.Should().OnlyContain(thing =>
            IsEdgeTo(composed, thing.Id, WillowBend.HazardAssessmentArchetypeId));
    }

    [Fact]
    public void Two_assessments_of_one_hazard_are_refused()
    {
        var refusal = Assert.Throws<SubmissionError>(() => Compose(WillowBend.Submission() with
        {
            Hazards =
            [
                new SubmittedHazard { HazardType = "river-flood" },
                new SubmittedHazard { HazardType = " River-Flood " },
            ],
        }));

        refusal.Message.Should().Contain("river-flood").And.Contain("River-Flood");
    }

    // One source is one Thing, so a second description has nowhere to land. Keeping the first silently would
    // leave the planner believing the second was recorded, which is the fault the whole submission path
    // refuses everywhere else.
    [Fact]
    public void One_source_described_two_ways_is_refused()
    {
        var refusal = Assert.Throws<SubmissionError>(() => Compose(WillowBend.Submission() with
        {
            Hazards =
            [
                new SubmittedHazard
                {
                    HazardType = "river-flood",
                    Source = new SubmittedDataSource { Name = "Portal", CoverageDescription = "Rivers." },
                },
                new SubmittedHazard
                {
                    HazardType = "wildfire",
                    Source = new SubmittedDataSource { Name = "Portal", CoverageDescription = "Forests." },
                },
            ],
        }));

        refusal.Message.Should().Contain("Rivers.").And.Contain("Forests.");
    }

    // A second mention that adds no description is the same source said again, not a disagreement.
    [Fact]
    public void One_source_mentioned_again_without_a_description_is_accepted()
    {
        var composed = Compose(WillowBend.Submission());

        Named(composed, "National flood portal").Properties["coverageDescription"].Value
            .Should().Be("Mainland river catchments, updated yearly.");
    }

    [Fact]
    public void A_submission_naming_no_hazards_mints_none()
    {
        var composed = Compose(WillowBend.Submission() with { Hazards = null });

        Hazards(composed).Should().BeEmpty();
        composed.Fragment.Relationships.Should()
            .NotContain(edge => edge.Target == WillowBend.HazardAssessmentArchetypeId);
    }

    [Fact]
    public void A_hazard_named_without_a_source_still_mints()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Hazards = [new SubmittedHazard { HazardType = "landslide" }],
        });

        Relates(composed, Hazards(composed).Single().Id, WillowBend.AssessesPredicateId,
            WillowBend.TermId("landslide")).Should().BeTrue();
        composed.Fragment.Relationships.Should()
            .NotContain(edge => edge.Target == WillowBend.DataSourceArchetypeId);
    }

    [Fact]
    public void The_study_relates_to_its_site_by_studies()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Relationships.Should().ContainSingle(edge =>
            edge.Subject == composed.StudyId
            && edge.Predicate == WillowBend.StudiesPredicateId
            && edge.Target == composed.SiteId);
    }

    [Fact]
    public void The_study_carries_the_flag_every_reader_finds_it_by()
    {
        var composed = Compose(WillowBend.Submission());

        Thing(composed, composed.StudyId).Properties[SubmissionFragmentComposer.SiteStudyFlag]
            .Value.Should().Be(true);
    }

    [Fact]
    public void Stated_area_stays_on_the_site_and_measured_area_on_the_parcel()
    {
        var composed = Compose(WillowBend.Submission());

        var site = Thing(composed, composed.SiteId);
        site.Properties["statedAreaHectares"].Value.Should().Be(24.0);
        site.Properties.Should().NotContainKey("measuredAreaHectares",
            "the measured area belongs to the parcel that encloses it, not to the site");

        Thing(composed, composed.ParcelId!.Value).Properties["measuredAreaHectares"]
            .Value.Should().BeOfType<double>().Which.Should().BeApproximately(23.4, 0.01);
    }

    [Fact]
    public void The_site_holds_its_parcel_as_a_thing_of_its_own()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Relationships.Should().ContainSingle(edge =>
            edge.Subject == composed.SiteId
            && edge.Predicate == WillowBend.HasPredicateId
            && edge.Target == composed.ParcelId!.Value);
    }

    // A study used to declare its own computed outputs, and named one no service writes. The archetype
    // declares every one of them, so a study that is the archetype resolves them through inheritance and
    // a declaration here could only disagree with it.
    [Fact]
    public void The_study_declares_no_computed_output_of_its_own()
    {
        var composed = Compose(WillowBend.Submission());

        Thing(composed, composed.StudyId).Properties.Keys
            .Should().Equal(SubmissionFragmentComposer.SiteStudyFlag);
    }

    [Fact]
    public void Every_thing_a_submission_mints_is_its_archetype()
    {
        var composed = Compose(WillowBend.Submission());

        IsEdgeTo(composed, composed.SiteId, WillowBend.SiteArchetypeId).Should().BeTrue();
        IsEdgeTo(composed, composed.StudyId, WillowBend.SiteStudyArchetypeId).Should().BeTrue();
        IsEdgeTo(composed, composed.ParcelId!.Value, WillowBend.ParcelArchetypeId).Should().BeTrue();
    }

    [Fact]
    public void A_value_the_planner_has_not_given_is_absent_rather_than_zero()
    {
        var submission = WillowBend.Submission();
        var composed = Compose(submission with { Site = submission.Site! with { Population = null } });

        Thing(composed, composed.SiteId).Properties.Should().NotContainKey("population");
    }

    [Fact]
    public void A_submission_with_no_parcel_drawn_yet_mints_none()
    {
        var composed = Compose(WillowBend.Submission() with { Parcel = null });

        composed.ParcelId.Should().BeNull();
        composed.Fragment.Things.Should().NotContain(thing => thing.Properties.ContainsKey("boundary"),
            "a boundary is what a parcel is, so nothing carrying one may exist when none was drawn");
        composed.Fragment.Relationships.Should().NotContain(edge => edge.Target == WillowBend.ParcelArchetypeId);
    }

    [Fact]
    public void A_submission_mints_a_record_of_its_own_arrival()
    {
        var composed = Compose(WillowBend.Submission());

        Record(composed).Properties["submissionId"].Value.Should().Be(WillowBend.SubmissionId);
        Record(composed).Properties["submittedAt"].Value.Should().Be(WillowBend.ArrivedAt);
        IsEdgeTo(composed, Record(composed).Id, WillowBend.SubmissionArchetypeId).Should().BeTrue();
    }

    // A promotion carries the group reachable from the site through `has` and `studies`, and takes with it
    // every edge a member of that group asserts. The record is resolved after the copy has landed, so a
    // copy of it would read as waiting for ever — it reaches the site through neither predicate, and the
    // site does not assert the edge that reaches it.
    [Fact]
    public void The_record_proposes_the_site_rather_than_holding_it()
    {
        var composed = Compose(WillowBend.Submission());
        var record = Record(composed);

        Relates(composed, record.Id, WillowBend.ProposesPredicateId, composed.SiteId).Should().BeTrue();
        Holds(composed, record.Id, composed.SiteId).Should().BeFalse();
        Relates(composed, composed.SiteId, WillowBend.StudiesPredicateId, record.Id).Should().BeFalse();
        composed.Fragment.Relationships.Should().NotContain(edge => edge.Target == record.Id,
            "nothing points at the record, so no group carrying another Thing can reach it");
    }

    // A wizard saves as the planner fills the form in and a fragment upserts, so a time written on every
    // save would record the last save rather than the arrival.
    [Fact]
    public void A_submission_posted_again_writes_no_arrival_time()
    {
        var composed = Compose(WillowBend.Submission(), arrivedAt: null);

        Record(composed).Properties.Should().ContainKey("submissionId").And.NotContainKey("submittedAt");
    }

    [Fact]
    public void The_record_of_one_submission_is_one_thing_however_often_it_is_posted()
    {
        var first = Compose(WillowBend.Submission(), WillowBend.ArrivedAt);
        var again = Compose(WillowBend.Submission(), arrivedAt: null);

        Record(again).Id.Should().Be(Record(first).Id);
    }

    [Fact]
    public void A_record_carries_no_disposition_when_it_arrives()
    {
        var composed = Compose(WillowBend.Submission());

        Record(composed).Properties.Keys.Should().BeEquivalentTo("submissionId", "submittedAt");
    }

    [Fact]
    public void The_same_submission_lands_on_the_same_things_every_time()
    {
        var first = Compose(WillowBend.Submission());
        var again = Compose(WillowBend.Submission());

        again.SiteId.Should().Be(first.SiteId);
        again.StudyId.Should().Be(first.StudyId);
        again.ParcelId.Should().Be(first.ParcelId);
    }

    [Fact]
    public void A_different_submission_lands_on_things_of_its_own()
    {
        var other = Compose(WillowBend.Submission() with { SubmissionId = "willow-bend-2026-09" });

        other.SiteId.Should().NotBe(Compose(WillowBend.Submission()).SiteId);
    }

    [Fact]
    public void The_boundary_is_stored_as_geojson_with_longitude_first_and_the_ring_closed()
    {
        var composed = Compose(WillowBend.Submission());

        var written = Thing(composed, composed.ParcelId!.Value).Properties["boundary"];
        written.TypeInfo.Should().Be(VosTypeNames.GeoJson,
            "the Parcel archetype declares the boundary as GeoJSON, and an instance writing text would "
            + "shadow that declaration with a weaker one");

        var boundary = (string)written.Value!;
        boundary.Should().StartWith("""{"type":"Polygon","coordinates":[[[-8.416519,39.4990248]""");
        boundary.Should().EndWith("""[-8.416519,39.4990248]]]}""");
    }

    [Fact]
    public void A_predicate_the_model_does_not_hold_is_carried_by_the_fragment()
    {
        var minted = new PredicateIdentity("studies", Guid.NewGuid(), Minted: true);

        var composed = SubmissionFragmentComposer.Compose(
            WillowBend.Submission() with { Parcel = null },
            WillowBend.KnownPredicates with { Studies = minted },
            WillowBend.KnownArchetypes,
            WillowBend.KnownVocabulary,
            WillowBend.ArrivedAt);

        composed.Fragment.Things.Should().Contain(thing => thing.Id == minted.Id && thing.Name == "studies");
    }

    [Fact]
    public void A_predicate_the_model_holds_is_used_as_it_stands()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Should().NotContain(thing => thing.Id == WillowBend.StudiesPredicateId);
    }

    [Theory]
    [InlineData(null, "submissionId")]
    [InlineData("", "submissionId")]
    public void A_submission_without_an_identifier_is_refused(string? submissionId, string named)
    {
        var refusal = Refusal(WillowBend.Submission() with { SubmissionId = submissionId });

        refusal.Message.Should().Contain(named);
    }

    [Fact]
    public void A_submission_without_a_site_is_refused()
    {
        Refusal(WillowBend.Submission() with { Site = null }).Message.Should().Contain("'site'");
    }

    [Fact]
    public void A_site_without_a_name_is_refused()
    {
        var submission = WillowBend.Submission();

        Refusal(submission with { Site = submission.Site! with { Name = null } })
            .Message.Should().Contain("site.name");
    }

    [Fact]
    public void A_boundary_of_two_corners_is_refused()
    {
        var submission = WillowBend.Submission();
        var twoCorners = submission.Parcel! with { Boundary = [.. submission.Parcel.Boundary!.Take(2)] };

        Refusal(submission with { Parcel = twoCorners }).Message.Should().Contain("at least three");
    }

    [Fact]
    public void A_parcel_without_a_boundary_is_refused()
    {
        var submission = WillowBend.Submission();

        Refusal(submission with { Parcel = submission.Parcel! with { Boundary = null } })
            .Message.Should().Contain("parcel.boundary");
    }

    [Fact]
    public void A_boundary_source_outside_the_vocabulary_is_refused()
    {
        var submission = WillowBend.Submission();

        var refusal = Refusal(submission with { Parcel = submission.Parcel! with { BoundarySource = "drawn" } });

        refusal.Message.Should().Contain("drawn-by-hand",
            "the message has to name what would have been accepted, or a typo reads as provenance");
    }

    [Fact]
    public void A_parcel_without_a_boundary_source_is_refused()
    {
        var submission = WillowBend.Submission();

        Refusal(submission with { Parcel = submission.Parcel! with { BoundarySource = null } })
            .Message.Should().Contain("parcel.boundarySource");
    }

    private static SubmissionError Refusal(Submission submission) =>
        Assert.Throws<SubmissionError>(() => Compose(submission));

    // Coverage is walked from the site outwards through `isIn`, so a site related to no Place reaches no
    // Place, and every source reads as covering nowhere. That is indistinguishable from a model holding no
    // source at all: the run reports neither a resolved source nor an unresolved one, and a planner is told
    // nothing was found rather than that nothing was looked for.
    [Fact]
    public void The_site_is_related_to_the_root_place_so_a_covering_source_can_be_selected_for_it()
    {
        var composed = Compose(WillowBend.Submission());
        var site = Named(composed, "Willow Bend");

        Relates(composed, site.Id, WillowBend.IsInPredicateId, WillowBend.RootPlaceId).Should().BeTrue();
    }

    // A source covering the root covers every site, so one edge is all a global source needs. Narrower
    // Places are not minted here: a country is an open set nobody enumerates, and `country` stays text.
    [Fact]
    public void No_place_is_created_for_the_country_the_submission_named()
    {
        var composed = Compose(WillowBend.Submission());

        composed.Fragment.Things.Should().NotContain(thing => thing.Name == "Portugal");
    }

    // The edge is written once however the submission arrives again, the same rule every other edge the
    // producer writes follows — a fragment upserts, so a second copy would be a second edge to one Place.
    [Fact]
    public void The_site_reaches_the_root_place_exactly_once()
    {
        var composed = Compose(WillowBend.Submission());
        var site = Named(composed, "Willow Bend");

        composed.Fragment.Relationships
            .Count(edge => edge.Subject == site.Id && edge.Predicate == WillowBend.IsInPredicateId)
            .Should().Be(1);
    }
}
