using FluentAssertions;
using vos.ManagedMicroservice.Phloem.Configuration;
using Xunit;

namespace vos.ManagedMicroservice.Phloem.Tests;

// The archetype vocabulary is config-driven (pushed by Mycelium at launch); CliArgs binds it, defaulting
// each name when its arg is absent.
public class CliArgsTests
{
    [Fact]
    public void Parse_WithoutArchetypeArgs_UsesDefaults()
    {
        var args = CliArgs.Parse(new[] { "--port=7410", "--myceliumUrl=http://localhost:7243" })!;

        args.Model.Connection.Should().Be("PlatformServiceConnection");
        args.Model.Service.Should().Be("Service");
        args.Model.Pipeline.Should().Be("Pipeline");
        args.Model.PipelineWire.Should().Be("PipelineWire");
        args.Model.NodeRun.Should().Be("NodeRun");
    }

    [Fact]
    public void Parse_WithArchetypeArgs_OverridesVocabulary()
    {
        var args = CliArgs.Parse(new[]
        {
            "--port=7410", "--myceliumUrl=http://localhost:7243",
            "--connectionArchetype=Conn", "--pipelineArchetype=Flow", "--pipelineWireArchetype=Wire",
        })!;

        args.Model.Connection.Should().Be("Conn");
        args.Model.Pipeline.Should().Be("Flow");
        args.Model.PipelineWire.Should().Be("Wire");
        args.Model.Service.Should().Be("Service"); // unspecified → default
    }

    [Fact]
    public void Parse_MissingRequiredArgs_ReturnsNull()
    {
        CliArgs.Parse(new[] { "--myceliumUrl=http://localhost:7243" }).Should().BeNull();
    }
}
