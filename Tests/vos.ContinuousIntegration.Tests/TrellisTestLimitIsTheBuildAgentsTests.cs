using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The 'Test Trellis' step in azure-pipelines.yml says why the limit lives on the build's command line.
public class TrellisTestLimitIsTheBuildAgentsTests
{
    private const int VitestDefaultLimitMilliseconds = 5_000;

    private static readonly string Trellis = Path.Combine(RepositoryRoot.Find(), "vos.Trellis");

    [Fact]
    public void The_build_runs_trellis_tests_with_a_limit_longer_than_vitests_default()
    {
        var pipeline = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "azure-pipelines.yml"));
        var step = Regex.Match(pipeline, @"- script: (?<command>.+)\r?\n\s+displayName: 'Test Trellis'");
        Assert.True(step.Success, "the pipeline has no step named 'Test Trellis' for this to read");

        var limit = Regex.Match(step.Groups["command"].Value, @"--testTimeout[= ](?<milliseconds>\d+)");
        Assert.True(limit.Success, $"the Trellis test step names no --testTimeout: {step.Groups["command"].Value}");
        Assert.True(int.Parse(limit.Groups["milliseconds"].Value) > VitestDefaultLimitMilliseconds,
            $"the Trellis test step's limit is not longer than vitest's default: {limit.Value}");
    }

    [Fact]
    public void A_run_on_a_developers_machine_keeps_vitests_default_limit()
    {
        var configuration = File.ReadAllText(Path.Combine(Trellis, "vite.config.ts"));

        Assert.DoesNotContain("testTimeout", configuration);
    }

    [Fact]
    public void No_test_file_sets_its_own_testTimeout()
    {
        var testFiles = Directory.EnumerateFiles(Path.Combine(Trellis, "src"), "*.test.*", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(testFiles);

        var settingTheirOwn = testFiles
            .Where(file => File.ReadAllText(file).Contains("testTimeout"))
            .Select(file => Path.GetRelativePath(Trellis, file))
            .ToList();

        Assert.True(settingTheirOwn.Count == 0,
            "the build's limit covers every test on the agent, and a limit a file sets for itself also "
            + $"applies on a developer's machine, where it hides a test that hangs: {string.Join("; ", settingTheirOwn)}");
    }
}
