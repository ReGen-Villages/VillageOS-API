using System.Xml.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The option written with a ":suggestion" suffix silenced both the unused-parameter and the unread-assignment rules in every build.
public class TheBuildReportsUnusedCodeTests
{
    private static readonly string Root = RepositoryRoot.Find();

    private static Dictionary<string, string> CSharpSettings()
    {
        var settings = new Dictionary<string, string>();
        var inCSharpSection = false;
        foreach (var line in File.ReadAllLines(Path.Combine(Root, ".editorconfig")).Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
                inCSharpSection = line == "[*.cs]";
            else if (inCSharpSection && !line.StartsWith('#') && line.Split('=', 2) is [var key, var value])
                settings[key.Trim()] = value.Trim();
        }
        return settings;
    }

    [Theory]
    [InlineData("IDE0051")]
    [InlineData("IDE0052")]
    [InlineData("IDE0059")]
    [InlineData("IDE0060")]
    public void Each_unused_code_rule_is_reported_as_a_warning(string rule)
    {
        Assert.Equal("warning", CSharpSettings().GetValueOrDefault($"dotnet_diagnostic.{rule}.severity"));
    }

    [Fact]
    public void The_unused_parameter_option_reaches_every_parameter_and_carries_no_severity_suffix()
    {
        Assert.Equal("all", CSharpSettings().GetValueOrDefault("dotnet_code_quality_unused_parameters"));
    }

    [Fact]
    public void Every_build_applies_the_code_style_rules_not_only_the_editor()
    {
        var properties = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));

        Assert.Equal("true", properties.Descendants("EnforceCodeStyleInBuild").SingleOrDefault()?.Value);
    }
}
