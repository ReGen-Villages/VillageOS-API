using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

public class TendrilIsPublishedOnlyFromItsTagTests
{
    private const string ReleaseCondition =
        "and(succeeded(), startsWith(variables['Build.SourceBranch'], 'refs/tags/tendril-v'))";

    private static string Pipeline() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "azure-pipelines.yml"));

    private static string[] Steps() => Regex.Split(Pipeline(), @"\r?\n(?=  - )");

    private static string[] PublishingSteps() =>
        Steps().SkipWhile(step => !step.Contains("checkReleaseTag.mjs")).ToArray();

    [Fact]
    public void A_tendril_tag_starts_the_build()
    {
        Assert.Matches(@"(?m)^  tags:\r?\n    include:\r?\n      - tendril-v\*$", Pipeline());
    }

    [Fact]
    public void The_tag_is_checked_before_anything_is_published()
    {
        var steps = Steps().ToList();
        var check = steps.FindIndex(step => step.Contains("checkReleaseTag.mjs"));
        var publish = steps.FindIndex(step => step.Contains("npm publish"));

        Assert.True(check >= 0, "the pipeline has no step that checks the release tag");
        Assert.True(publish > check, "npm publish runs before the release tag is checked, or not at all");
    }

    [Fact]
    public void Every_publishing_step_runs_only_for_a_tendril_tag_after_every_earlier_step_passed()
    {
        var publishing = PublishingSteps();
        Assert.NotEmpty(publishing);

        Assert.All(publishing, step => Assert.Contains($"condition: {ReleaseCondition}", step));
    }

    [Fact]
    public void No_step_that_builds_or_tests_runs_after_the_publishing_steps_start()
    {
        Assert.DoesNotContain(PublishingSteps(), step => step.Contains("condition: always()"));
    }
}
