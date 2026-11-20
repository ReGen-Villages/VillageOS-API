using System.Text.RegularExpressions;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

// The model reference tool in the VillageOS repository reads the composer as text with this pattern, so a
// name constant it cannot read there stops that build even when the constant's value is right.
public class SubmissionFragmentComposerSourceTests
{
    private const string TheToolsNameConstantPattern = "const string (\\w+)PredicateName = \"([^\"]+)\"";

    [Fact]
    public void The_model_reference_tool_reads_a_literal_name_for_each_predicate_the_composer_finds_by_name()
    {
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot.Find(), "vos.Service.Intake", "Services", "SubmissionFragmentComposer.cs"));

        Regex.Matches(source, TheToolsNameConstantPattern)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value)
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["Has"] = "has", ["Is"] = "is" });
    }
}
