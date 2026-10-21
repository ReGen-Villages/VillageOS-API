using FluentAssertions;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

public class SubmissionReaderTests
{
    [Fact]
    public void A_field_the_service_does_not_write_is_refused_by_name()
    {
        var refusal = Assert.Throws<SubmissionError>(() => SubmissionReader.Read(
            """{"submissionId":"willow-bend-2026-08","budgetEuros":250000}"""));

        refusal.Message.Should().Contain("budgetEuros",
            "a field accepted and quietly dropped leaves the planner believing it was recorded");
    }

    [Fact]
    public void A_share_is_read_as_a_percent_and_refused_under_its_shortened_name()
    {
        string Sharing(string field) =>
            "{\"submissionId\":\"" + WillowBend.SubmissionId + "\",\"allocations\":[{\"category\":\"residential\",\"" + field + "\":22}]}";

        var submission = SubmissionReader.Read(Sharing("sharePercent"));
        var refusal = Assert.Throws<SubmissionError>(() => SubmissionReader.Read(Sharing("sharePct")));

        submission.Allocations!.Single().SharePercent.Should().Be(22);
        refusal.Message.Should().Contain("sharePct");
    }

    [Fact]
    public void A_document_that_is_not_a_submission_is_refused()
    {
        Assert.Throws<SubmissionError>(() => SubmissionReader.Read("{ this is not json"));
    }

    [Fact]
    public void An_empty_document_is_refused()
    {
        Assert.Throws<SubmissionError>(() => SubmissionReader.Read("null"))
            .Message.Should().Contain("empty");
    }

    [Fact]
    public void Field_names_are_read_however_they_are_capitalised()
    {
        var submission = SubmissionReader.Read(
            ("{'SubmissionId':'" + WillowBend.SubmissionId + "','site':{'Name':'Willow Bend','latitude':39.5012}}")
            .Replace('\'', '"'));

        submission.SubmissionId.Should().Be(WillowBend.SubmissionId);
        submission.Site!.Name.Should().Be("Willow Bend");
        submission.Site.Latitude.Should().Be(39.5012);
    }
}
