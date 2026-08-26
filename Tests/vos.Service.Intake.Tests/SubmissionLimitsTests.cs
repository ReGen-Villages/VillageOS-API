using System.Globalization;
using FluentAssertions;
using vos.Service.Intake;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

/// <summary>What a submission may contain, checked where a document becomes a submission. The route is
/// anonymous, so every bound here is a bound on what a stranger can make this service hold.</summary>
/// <remarks>A shape is written with apostrophes where JSON wants quotation marks, so the fragments read as
/// the objects they are rather than as escaping.</remarks>
public class SubmissionLimitsTests
{
    private static SubmissionError Refusing(string shape) =>
        Assert.Throws<SubmissionError>(() => SubmissionReader.Read(Document(shape)));

    private static string Document(string shape) =>
        $"{{\"submissionId\":\"{WillowBend.SubmissionId}\",{shape.Replace('\'', '"')}}}";

    // JSON has one spelling of a number, and the culture the tests run under has its own.
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // Every Thing a submission mints derives its identity from this, and two submissions carrying one
    // identifier are one site. Where anybody may post, an identifier anybody could arrive at is a way to
    // write over a submission somebody else made.
    [Fact]
    public void An_identifier_anybody_could_arrive_at_is_refused()
    {
        var refusal = Assert.Throws<SubmissionError>(() => SubmissionReader.Read(
            """{"submissionId":"willow-bend-2026-08","site":{"name":"Willow Bend"}}"""));

        refusal.Message.Should().Contain("submissionId");
    }

    // A submission carrying none at all is refused where it is composed, naming what every identifier
    // derives from. Refusing it here instead would answer a missing field with a rule about its shape.
    [Fact]
    public void A_submission_carrying_no_identifier_gets_past_the_bound_on_its_shape()
    {
        SubmissionReader.Read("""{"site":{"name":"Willow Bend"}}""")
            .SubmissionId.Should().BeNull();
    }

    [Fact]
    public void The_identifier_a_wizard_generates_is_accepted()
    {
        SubmissionReader.Read(Document("'site':{'name':'Willow Bend'}"))
            .SubmissionId.Should().Be(WillowBend.SubmissionId);
    }

    [Theory]
    [InlineData("'site':{'name':'@@'}", "site.name")]
    [InlineData("'project':{'name':'@@'}", "project.name")]
    [InlineData("'project':{'name':'Willow Bend Regeneration','country':'@@'}", "project.country")]
    [InlineData("'project':{'name':'Willow Bend Regeneration','nearestCity':'@@'}", "project.nearestCity")]
    [InlineData("'contact':{'name':'@@'}", "contact.name")]
    [InlineData("'contact':{'name':'Ana','emailAddress':'@@'}", "contact.emailAddress")]
    [InlineData("'contact':{'name':'Ana','phoneNumber':'@@'}", "contact.phoneNumber")]
    [InlineData("'contact':{'name':'Ana','relationshipToProject':'@@'}", "contact.relationshipToProject")]
    [InlineData("'allocations':[{'category':'@@'}]", "allocation.category")]
    [InlineData("'hazards':[{'hazardType':'@@'}]", "hazard.hazardType")]
    [InlineData("'hazards':[{'hazardType':'riverFlood','source':{'name':'@@'}}]", "hazard.source.name")]
    [InlineData("'parcel':{'boundarySource':'@@'}", "parcel.boundarySource")]
    public void Text_beyond_what_a_field_holds_is_refused_by_name(string shape, string field)
    {
        var overLong = new string('x', SubmissionLimits.LongestText + 1);

        var refusal = Refusing(shape.Replace("@@", overLong));

        refusal.Message.Should().Contain(field);
        refusal.Message.Should().NotContain(overLong,
            "a refusal names the field to correct and never quotes what was submitted back");
    }

    [Theory]
    [InlineData("'project':{'name':'Willow Bend Regeneration','existingDataNotes':'@@'}",
        "project.existingDataNotes")]
    [InlineData("'hazards':[{'hazardType':'riverFlood','source':{'name':'Portal','coverageDescription':'@@'}}]",
        "hazard.source.coverageDescription")]
    public void Prose_beyond_what_a_field_holds_is_refused_by_name(string shape, string field)
    {
        var overLong = new string('x', SubmissionLimits.LongestProse + 1);

        Refusing(shape.Replace("@@", overLong)).Message.Should().Contain(field);
    }

    [Theory]
    [InlineData(90.1)]
    [InlineData(-90.1)]
    public void A_latitude_off_the_globe_is_refused(double latitude)
    {
        Refusing("'site':{'name':'W','latitude':" + Number(latitude) + "}")
            .Message.Should().Contain("site.latitude");
    }

    [Theory]
    [InlineData(180.1)]
    [InlineData(-180.1)]
    public void A_longitude_off_the_globe_is_refused(double longitude)
    {
        Refusing("'site':{'name':'W','longitude':" + Number(longitude) + "}")
            .Message.Should().Contain("site.longitude");
    }

    [Fact]
    public void A_boundary_corner_off_the_globe_is_refused()
    {
        Refusing("'parcel':{'boundarySource':'drawn-by-hand','boundary':"
                 + "[{'latitude':39.5,'longitude':-8.4},{'latitude':91.0,'longitude':-8.4}]}")
            .Message.Should().Contain("parcel.boundary");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SubmissionLimits.LargestAreaHectares + 1)]
    public void An_area_no_landholding_could_measure_is_refused(double hectares)
    {
        Refusing("'site':{'name':'W','statedAreaHectares':" + Number(hectares) + "}")
            .Message.Should().Contain("site.statedAreaHectares");
    }

    // An allocation's area is a formula the model works out from the share and the parcel, so the wire does
    // not take one. A caller that sends it is told which field by name rather than having it accepted and
    // dropped, which is the rule this service holds for every field it does not write (Bug #6762).
    [Fact]
    public void An_area_the_model_works_out_for_itself_is_refused_by_name()
    {
        Refusing("'allocations':[{'category':'residential','sharePct':22,'allocatedAreaHectares':5.28}]")
            .Message.Should().Contain("allocatedAreaHectares");
    }

    [Fact]
    public void A_population_no_site_could_hold_is_refused()
    {
        Refusing("'site':{'name':'W','population':-1}").Message.Should().Contain("site.population");
    }

    [Fact]
    public void A_household_nobody_lives_in_is_refused()
    {
        Refusing("'site':{'name':'W','householdSize':0}").Message.Should().Contain("site.householdSize");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_share_that_is_not_a_percentage_is_refused(double share)
    {
        Refusing("'allocations':[{'category':'residential','sharePct':" + Number(share) + "}]")
            .Message.Should().Contain("allocation.sharePct");
    }

    [Fact]
    public void More_corners_than_a_boundary_is_drawn_with_is_refused()
    {
        var corners = string.Join(",", Enumerable.Repeat(
            "{'latitude':39.5,'longitude':-8.4}", SubmissionLimits.MostBoundaryCorners + 1));

        Refusing("'parcel':{'boundarySource':'drawn-by-hand','boundary':[" + corners + "]}")
            .Message.Should().Contain("parcel.boundary");
    }

    [Fact]
    public void More_shares_than_the_land_is_divided_into_is_refused()
    {
        var allocations = string.Join(",", Enumerable.Repeat(
            "{'category':'residential'}", SubmissionLimits.MostAllocations + 1));

        Refusing("'allocations':[" + allocations + "]").Message.Should().Contain("allocations");
    }

    [Fact]
    public void More_assessments_than_a_site_is_judged_on_is_refused()
    {
        var hazards = string.Join(",", Enumerable.Repeat(
            "{'hazardType':'riverFlood'}", SubmissionLimits.MostHazards + 1));

        Refusing("'hazards':[" + hazards + "]").Message.Should().Contain("hazards");
    }

    [Fact]
    public void The_worked_example_passes_every_bound()
    {
        var act = () => SubmissionLimits.Enforce(WillowBend.Submission());

        act.Should().NotThrow("the design document's worked example is what a legitimate submission looks like");
    }
}
