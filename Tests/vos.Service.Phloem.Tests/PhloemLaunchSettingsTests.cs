using FluentAssertions;
using Microsoft.Extensions.Configuration;
using vos.Service.Phloem.Configuration;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The settings every service shares are covered once in vos.Service.Shared.Tests. These cover the
// pipeline model archetype names, which only Phloem has.
public class PhloemLaunchSettingsTests
{
    private static readonly string[] RequiredFlags =
        ["--port=7111", "--myceliumUrl=https://localhost:7243"];

    private static string[] FlagsWith(params string[] extra) => [.. RequiredFlags, .. extra];

    private static IConfiguration ConfigurationFrom(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build();

    [Fact]
    public void Parse_WhenACommonSettingIsMissing_ReturnsNull() =>
        PhloemLaunchSettings.Parse(["--pipelineArchetype=Renamed"]).Should().BeNull();

    [Fact]
    public void Parse_CarriesTheCommonSettingsThrough()
    {
        var result = PhloemLaunchSettings.Parse(FlagsWith("--token=my.jwt.token"));

        result.Should().NotBeNull();
        result!.Service.Port.Should().Be(7111);
        result.Service.MyceliumUrl.Should().Be("https://localhost:7243");
        result.Service.Token.Should().Be("my.jwt.token");
    }

    [Fact]
    public void Parse_WithoutArchetypeFlags_KeepsTheDefaultVocabulary()
    {
        var result = PhloemLaunchSettings.Parse(RequiredFlags);

        result.Should().NotBeNull();
        result!.Model.Should().BeEquivalentTo(new PipelineModelOptions());
    }

    [Fact]
    public void Parse_WithArchetypeFlags_RenamesTheWholeVocabulary()
    {
        var result = PhloemLaunchSettings.Parse(FlagsWith(
            "--connectionArchetype=Link",
            "--serviceArchetype=Worker",
            "--pipelineArchetype=Flow",
            "--pipelineNodeArchetype=Step",
            "--pipelineInputArchetype=Start",
            "--pipelineOutputArchetype=End",
            "--portArchetype=Socket",
            "--pipelineWireArchetype=Wire",
            "--pipelineRunArchetype=FlowRun",
            "--nodeRunArchetype=StepRun"));

        result.Should().NotBeNull();
        result!.Model.Connection.Should().Be("Link");
        result.Model.Service.Should().Be("Worker");
        result.Model.Pipeline.Should().Be("Flow");
        result.Model.PipelineNode.Should().Be("Step");
        result.Model.PipelineInput.Should().Be("Start");
        result.Model.PipelineOutput.Should().Be("End");
        result.Model.Port.Should().Be("Socket");
        result.Model.PipelineWire.Should().Be("Wire");
        result.Model.PipelineRun.Should().Be("FlowRun");
        result.Model.NodeRun.Should().Be("StepRun");
    }

    [Fact]
    public void Parse_RenamingOneArchetype_LeavesTheRestAtTheirDefaults()
    {
        var defaults = new PipelineModelOptions();

        var result = PhloemLaunchSettings.Parse(FlagsWith("--pipelineArchetype=Flow"));

        result.Should().NotBeNull();
        result!.Model.Pipeline.Should().Be("Flow");
        result.Model.PipelineNode.Should().Be(defaults.PipelineNode);
        result.Model.Connection.Should().Be(defaults.Connection);
    }

    [Fact]
    public void Parse_ReadsArchetypeNamesFromConfiguration()
    {
        var configuration = ConfigurationFrom(
            ("Port", "7100"), ("MyceliumUrl", "http://mycelium"),
            ("PipelineArchetype", "ConfiguredFlow"));

        var result = PhloemLaunchSettings.Parse(Array.Empty<string>(), configuration);

        result.Should().NotBeNull();
        result!.Model.Pipeline.Should().Be("ConfiguredFlow");
    }

    [Fact]
    public void UsageMessage_NamesTheArchetypeFlags()
    {
        var usage = PhloemLaunchSettings.UsageMessage;

        usage.Should().Contain("--pipelineArchetype")
            .And.Contain("--nodeRunArchetype")
            .And.Contain("--myceliumUrl");
    }
}
