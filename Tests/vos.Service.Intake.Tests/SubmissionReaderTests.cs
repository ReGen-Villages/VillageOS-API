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
            """{"submissionId":"willow-bend-2026-08","contact":{"email":"someone@example.org"}}"""));

        refusal.Message.Should().Contain("contact",
            "a field accepted and quietly dropped leaves the planner believing it was recorded");
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
            """{"SubmissionId":"willow-bend-2026-08","site":{"Name":"Willow Bend","latitude":39.5012}}""");

        submission.SubmissionId.Should().Be("willow-bend-2026-08");
        submission.Site!.Name.Should().Be("Willow Bend");
        submission.Site.Latitude.Should().Be(39.5012);
    }
}
