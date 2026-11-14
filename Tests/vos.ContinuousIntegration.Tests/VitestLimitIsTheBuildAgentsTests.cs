using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The 'Test Trellis' step in azure-pipelines.yml says why the limit lives on the build's command line.
public class VitestLimitIsTheBuildAgentsTests
{
    private const int VitestDefaultLimitMilliseconds = 5_000;

    [Theory]
    [InlineData("Test Trellis")]
    [InlineData("Test Tendril")]
    public void The_build_runs_vitest_with_a_limit_longer_than_vitests_default(string stepName)
    {
        var pipeline = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "azure-pipelines.yml"));
        var step = Regex.Match(pipeline, $@"- script: (?<command>.+)\r?\n\s+displayName: '{stepName}'");
        Assert.True(step.Success, $"the pipeline has no step named '{stepName}' for this to read");

        var limit = Regex.Match(step.Groups["command"].Value, @"--testTimeout[= ](?<milliseconds>\d+)");
        Assert.True(limit.Success, $"the '{stepName}' step names no --testTimeout: {step.Groups["command"].Value}");
        Assert.True(int.Parse(limit.Groups["milliseconds"].Value) > VitestDefaultLimitMilliseconds,
            $"the '{stepName}' step's limit is not longer than vitest's default: {limit.Value}");
    }

    [Theory]
    [InlineData("vos.Trellis")]
    [InlineData("vos.Tendril")]
    public void A_run_on_a_developers_machine_keeps_vitests_default_limit(string folder)
    {
        var configuration = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), folder, "vite.config.ts"));

        Assert.DoesNotContain("testTimeout", configuration);
    }

    [Theory]
    [InlineData("vos.Trellis")]
    [InlineData("vos.Tendril")]
    public void No_test_file_sets_its_own_testTimeout(string folder)
    {
        var root = Path.Combine(RepositoryRoot.Find(), folder);
        var testFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.test.*", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(testFiles);

        var settingTheirOwn = testFiles
            .Where(file => File.ReadAllText(file).Contains("testTimeout"))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.True(settingTheirOwn.Count == 0,
            "the build's limit covers every test on the agent, and a limit a file sets for itself also "
            + $"applies on a developer's machine, where it hides a test that hangs: {string.Join("; ", settingTheirOwn)}");
    }
}
