using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

public class DotnetInstallIsPinnedTests
{
    private const string DotnetSdkVersionVariable = "dotnetSdkVersion";

    private static string Pipeline() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "azure-pipelines.yml"));

    [Fact]
    public void The_dotnet_sdk_is_pinned_to_one_exact_release()
    {
        var declaration = Regex.Match(Pipeline(), $@"- name: {DotnetSdkVersionVariable}\r?\n\s+value: '(?<version>[^']*)'");
        Assert.True(declaration.Success, $"the pipeline declares no {DotnetSdkVersionVariable} variable");

        Assert.Matches(@"^\d+\.\d+\.\d+$", declaration.Groups["version"].Value);
    }

    [Fact]
    public void Every_dotnet_install_takes_the_pinned_release_into_a_folder_named_for_it()
    {
        var installs = Regex.Matches(Pipeline(), @"task: UseDotNet@2(?:\r?\n(?!  - ).*)*")
            .Select(match => match.Value)
            .ToArray();
        Assert.NotEmpty(installs);

        Assert.All(installs, install =>
        {
            Assert.Contains($"version: '$({DotnetSdkVersionVariable})'", install);
            Assert.Contains($"installationPath: '$(Agent.ToolsDirectory)/dotnet-$({DotnetSdkVersionVariable})'", install);
        });
    }
}
