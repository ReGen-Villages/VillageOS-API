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
        SubmissionFragmentComposer.Compose(submission, WillowBend.KnownPredicates, WillowBend.KnownArchetypes);

    private static bool IsEdgeTo(ComposedSubmission composed, Guid subject, Guid archetype) =>
        composed.Fragment.Relationships.Any(edge =>
            edge.Subject == subject && edge.Predicate == WillowBend.IsPredicateId && edge.Target == archetype);

    private static FragmentThing Thing(ComposedSubmission composed, Guid id) =>
        composed.Fragment.Things.Single(thing => thing.Id == id);

    private static FragmentThing Named(ComposedSubmission composed, string name) =>
        composed.Fragment.Things.Single(thing => thing.Name == name);

    private static bool Holds(ComposedSubmission composed, Guid subject, Guid target) =>
        composed.Fragment.Relationships.Any(edge =>
            edge.Subject == subject && edge.Predicate == WillowBend.HasPredicateId && edge.Target == target);

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
        composed.Fragment.Things.Where(thing => thing.Properties.ContainsKey("allocationCategory"));

    [Fact]
    public void Each_allocation_is_a_thing_of_its_own_the_site_holds()
    {
        var composed = Compose(WillowBend.Submission());

        var allocations = Allocations(composed).ToList();
        allocations.Should().HaveCount(WillowBend.Submission().Allocations!.Count);
        allocations.Should().OnlyContain(thing =>
            composed.Fragment.Relationships.Any(edge =>
                edge.Subject == composed.SiteId
                && edge.Predicate == WillowBend.HasPredicateId
                && edge.Target == thing.Id));
        allocations.Should().OnlyContain(thing =>
            IsEdgeTo(composed, thing.Id, WillowBend.ProgrammeAllocationArchetypeId));
    }

    [Fact]
    public void An_allocation_writes_the_properties_its_archetype_declares()
    {
        var composed = Compose(WillowBend.Submission());

        var residential = Allocations(composed)
            .Single(thing => (string)thing.Properties["allocationCategory"].Value! == "Residential");
        residential.Properties["sharePct"].Value.Should().Be(22.0);
        residential.Properties["allocatedAreaHectares"].Value.Should().Be(5.28);
    }

    // Shares are normalised across the chosen categories further down the analysis, so a set that does not
    // reach a hundred is a wizard part-filled. Refusing it here would reject a submission mid-save.
    [Fact]
    public void Shares_that_do_not_add_to_a_hundred_are_taken_as_given()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "Residential", SharePct = 12 }],
        });

        Allocations(composed).Single().Properties["sharePct"].Value.Should().Be(12.0);
    }

    // Which categories roll into which footprint is configuration on the analysis node, so a project with
    // its own programme vocabulary must not need a change here.
    [Fact]
    public void A_category_outside_any_familiar_vocabulary_is_accepted()
    {
        var composed = Compose(WillowBend.Submission() with
        {
            Allocations = [new SubmittedAllocation { Category = "Silvopasture", SharePct = 100 }],
        });

        Allocations(composed).Single().Properties["allocationCategory"].Value.Should().Be("Silvopasture");
    }

    // Two shares of one category disagree about it and nothing here can say which was meant.
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
            Allocations(composed).ToDictionary(
                thing => (string)thing.Properties["allocationCategory"].Value!, thing => thing.Id);

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
        composed.Fragment.Things.Where(thing => thing.Properties.ContainsKey("hazardType"));

    // The whole point of the archetype change: a hazard says which source assessed it by hanging off that
    // source, so a reader can walk from one to the other. A name copied onto the hazard could be walked to
    // by nothing, and could disagree with the source's own with nothing to notice.
    [Fact]
    public void A_hazard_hangs_off_the_source_that_assessed_it()
    {
        var composed = Compose(WillowBend.Submission());

        var flood = Hazards(composed).Single(thing => (string)thing.Properties["hazardType"].Value! == "riverFlood");
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
                new SubmittedHazard { HazardType = "riverFlood" },
                new SubmittedHazard { HazardType = " RiverFlood " },
            ],
        }));

        refusal.Message.Should().Contain("riverFlood").And.Contain("RiverFlood");
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

        Hazards(composed).Single().Properties["hazardType"].Value.Should().Be("landslide");
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
            WillowBend.KnownArchetypes);

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
}
