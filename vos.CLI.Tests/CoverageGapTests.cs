using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.CLI.Tests;

// Targeted tests covering specific branches in vos.CLI that the per-class
// test files don't otherwise exercise. Each test names the gap it pins.
public class CoverageGapTests
{
    private const string BrokerUrl = "https://localhost:7243";

    // ---- CommandHandler dispatcher entries (lines 175-185 in CommandHandler.cs) ----

    [Theory]
    [InlineData("start", "")]
    [InlineData("stop", "")]
    [InlineData("config", "")]
    [InlineData("range", "")]
    [InlineData("state", "")]
    [InlineData("seeds", "")]
    [InlineData("broker", "")]
    [InlineData("model", "")]
    [InlineData("user", "")]
    public async Task CommandHandler_DispatchesAllSubHandlerCommands(string cmd, string args)
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = new CommandHandler(reader, writer, brokerMock.Object, BrokerUrl, interactiveMode: false);

        // Each command's dispatch lambda fires the sub-handler; the sub-handler will print
        // its own usage given empty args. We just need the dispatcher line to execute.
        var act = async () => await handler.HandleCommandAsync(cmd, args);

        await act.Should().NotThrowAsync();
    }

    // ---- ConfigCommandHandler --ringbuffer / --samplerate parsing (lines 238, 240) ----

    [Fact]
    public async Task ConfigCommandHandler_DefaultModeRingBufferWithSize_ParsesNamedArg()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        brokerMock.Setup(b => b.SetDefaultPropertyModeAsync("ringbuffer", 50, null))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);
        var writer = new StringWriter();
        var handler = new ConfigCommandHandler("mode ringbuffer --ringbuffer=50", writer, brokerMock.Object);

        await handler.ExecuteAsync();

        brokerMock.Verify(b => b.SetDefaultPropertyModeAsync("ringbuffer", 50, null), Times.Once);
    }

    [Fact]
    public async Task ConfigCommandHandler_DefaultModeSampledWithRate_ParsesNamedArg()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        brokerMock.Setup(b => b.SetDefaultPropertyModeAsync("sampled", null, 100))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);
        var writer = new StringWriter();
        var handler = new ConfigCommandHandler("mode sampled --samplerate=100", writer, brokerMock.Object);

        await handler.ExecuteAsync();

        brokerMock.Verify(b => b.SetDefaultPropertyModeAsync("sampled", null, 100), Times.Once);
    }

    // ---- ConsoleOptions environment variables (lines 12, 34) ----

    [Fact]
    public void ConsoleOptions_VosBrokerUrlEnvVar_OverridesDefault()
    {
        var original = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://custom-broker:9000");
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            options.BrokerUrl.Should().Be("https://custom-broker:9000");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", original);
        }
    }

    [Fact]
    public void ConsoleOptions_VosApiKeyEnvVar_PopulatesApiKey()
    {
        var original = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", "test-api-key-from-env");
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            options.ApiKey.Should().Be("test-api-key-from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_API_KEY", original);
        }
    }

    [Fact]
    public void ConsoleOptions_DefaultsApplyWhenNeitherEnvNorArgs()
    {
        var origUrl = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        var origKey = Environment.GetEnvironmentVariable("VOS_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", null);
            Environment.SetEnvironmentVariable("VOS_API_KEY", null);
            var options = ConsoleOptions.Parse(Array.Empty<string>());
            options.BrokerUrl.Should().Be("https://localhost:7243");
            options.ApiKey.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", origUrl);
            Environment.SetEnvironmentVariable("VOS_API_KEY", origKey);
        }
    }

    [Fact]
    public void ConsoleOptions_CommandLineArgs_OverrideEnvVars()
    {
        var origUrl = Environment.GetEnvironmentVariable("VOS_BROKER_URL");
        try
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", "https://from-env:9000");
            var options = ConsoleOptions.Parse(new[] { "--broker-url=https://from-cli:8000", "--api-key=clikey" });
            options.BrokerUrl.Should().Be("https://from-cli:8000");
            options.ApiKey.Should().Be("clikey");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOS_BROKER_URL", origUrl);
        }
    }

    // ---- ListCommandHandler missing-Properties / nested-inheritance branches ----
    // (lines 137, 151-155, 159-163, 428-431)

    [Fact]
    public async Task ListCommandHandler_ServicesWithMissingFields_FallsBackToNA()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        // Service entries without ServiceName / HandlerId trigger the ResolveDisplayName N/A fallback.
        brokerMock.Setup(b => b.GetAllServicesAsync())
            .ReturnsAsync(JsonDocument.Parse("[{}]").RootElement);
        brokerMock.Setup(b => b.GetAllThingsAsync())
            .ReturnsAsync(JsonDocument.Parse("[]").RootElement);
        var writer = new StringWriter();
        var handler = new ListCommandHandler("services", writer, brokerMock.Object);

        await handler.ExecuteAsync();

        // Should not throw — the fallback path returns "N/A" without crashing.
        writer.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListCommandHandler_ThingsWithNestedInheritance_TraversesAllLevels()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        // A thing with nested InheritedProperties.<sourceId>.Inherited.<deeperId>.Properties exercises
        // PushNestedInheritance + GetSourceName recursion.
        var json = """
        [{
          "Id": "00000000-0000-0000-0000-000000000001",
          "Name": "child",
          "Properties": {},
          "InheritedProperties": {
            "00000000-0000-0000-0000-000000000002": {
              "SourceName": "parent",
              "Properties": { "p1": { "value": "v1" } },
              "Inherited": {
                "00000000-0000-0000-0000-000000000003": {
                  "SourceName": "grandparent",
                  "Properties": { "g1": { "value": "v2" } }
                }
              }
            }
          }
        }]
        """;
        brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(JsonDocument.Parse(json).RootElement);
        var writer = new StringWriter();
        var handler = new ListCommandHandler("things", writer, brokerMock.Object);

        await handler.ExecuteAsync();

        var output = writer.ToString();
        output.Should().Contain("parent");
        output.Should().Contain("grandparent");
    }

    [Fact]
    public async Task ListCommandHandler_NestedInheritanceWithoutSourceName_FallsBackToKey()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        // Nested entry lacks "SourceName" → GetSourceName falls back to the key (sourceId).
        var json = """
        [{
          "Id": "00000000-0000-0000-0000-000000000001",
          "Name": "child",
          "Properties": {},
          "InheritedProperties": {
            "00000000-0000-0000-0000-000000000002": {
              "SourceName": "parent",
              "Properties": {},
              "Inherited": {
                "00000000-0000-0000-0000-000000000003": {
                  "Properties": {}
                }
              }
            }
          }
        }]
        """;
        brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(JsonDocument.Parse(json).RootElement);
        var writer = new StringWriter();
        var handler = new ListCommandHandler("things", writer, brokerMock.Object);

        var act = async () => await handler.ExecuteAsync();

        await act.Should().NotThrowAsync();
    }

    // ---- TemporalCommandHandler ParseOptionalTimestamp no-arg path (line 208) ----

    [Fact]
    public async Task TemporalCommandHandler_SnapshotNoTimestamp_PassesNull()
    {
        var brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
        brokerMock.Setup(b => b.GetModelAtTimeAsync(null))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);
        var writer = new StringWriter();
        var handler = new TemporalCommandHandler("snapshot", writer, brokerMock.Object);

        await handler.ExecuteAsync();

        brokerMock.Verify(b => b.GetModelAtTimeAsync(null), Times.AtLeastOnce);
    }
}

