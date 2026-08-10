using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>
/// What a submission has to put into the model. Each of the first four is the executable form of a clause
/// the platform's own fixture pins, so a site built here and a site built by an imported building model
/// stay the same shape and one reader serves both.
/// </summary>
public class SubmissionFragmentComposerTests
{
    private static ComposedSubmission Compose(Submission submission) =>
        SubmissionFragmentComposer.Compose(submission, WillowBend.KnownPredicates);

    private static FragmentThing Thing(ComposedSubmission composed, Guid id) =>
        composed.Fragment.Things.Single(thing => thing.Id == id);

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

    [Fact]
    public void A_computed_output_is_declared_with_its_type_and_no_value()
    {
        var composed = Compose(WillowBend.Submission());

        var declared = Thing(composed, composed.StudyId).Properties["energySelfSufficiencyPct"];
        declared.TypeInfo.Should().Be(VosTypeNames.Double);
        declared.Value.Should().BeNull();

        JsonSerializer.Serialize(declared).Should().Be("""{"typeInfo":"vos.Double"}""",
            "an envelope that carries no value declares the name without asserting anything, which is what "
            + "keeps an uncomputed study readable as uncomputed rather than as zero");
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
        composed.Fragment.Things.Should().HaveCount(2);
        composed.Fragment.Relationships.Should().ContainSingle();
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

        var boundary = (string)Thing(composed, composed.ParcelId!.Value).Properties["boundary"].Value!;
        boundary.Should().StartWith("""{"type":"Polygon","coordinates":[[[-8.416519,39.4990248]""");
        boundary.Should().EndWith("""[-8.416519,39.4990248]]]}""");
    }

    [Fact]
    public void A_predicate_the_model_does_not_hold_is_carried_by_the_fragment()
    {
        var minted = new PredicateIdentity("studies", Guid.NewGuid(), Minted: true);

        var composed = SubmissionFragmentComposer.Compose(
            WillowBend.Submission() with { Parcel = null },
            WillowBend.KnownPredicates with { Studies = minted });

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
