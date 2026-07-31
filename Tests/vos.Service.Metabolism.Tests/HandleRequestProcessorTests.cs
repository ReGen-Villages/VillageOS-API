using System.Text.Json;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace vos.Service.Metabolism.Tests;

public class HandleRequestProcessorTests
{
    private readonly HandleRequestProcessor _processor;
    private readonly Services.Metabolism _engine;

    public HandleRequestProcessorTests()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var myceliumLogger = new Mock<ILogger<MyceliumClient>>();
        var myceliumClient = new MyceliumClient(httpFactory.Object, myceliumLogger.Object, "http://localhost:0", "consumes");

        var engineLogger = new Mock<ILogger<Services.Metabolism>>();
        _engine = new Services.Metabolism(myceliumClient, engineLogger.Object, "consumes");

        var processorLogger = new Mock<ILogger<HandleRequestProcessor>>();
        _processor = new HandleRequestProcessor(_engine, processorLogger.Object);
    }

    #region ExtractConfig

    [Fact]
    public void ExtractConfig_AllPropertiesPresent_ReturnsFullConfig()
    {
        var json = JsonDocument.Parse("""
        {
            "quantity": 5.5,
            "propertyPath": "water_level",
            "unit": "litres",
            "frequencySeconds": 30,
            "startUtc": "2024-01-01T00:00:00Z",
            "endUtc": "2024-12-31T23:59:59Z"
        }
        """).RootElement;

        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", "TestSubject", "TestTarget", json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(5.5m);
        config.PropertyPath.Should().Be("water_level");
        config.Unit.Should().Be("litres");
        config.FrequencySeconds.Should().Be(30);
        config.StartUtc.Year.Should().Be(2024);
        config.EndUtc.Year.Should().Be(2024);
        config.SubjectName.Should().Be("TestSubject");
        config.StartDelaySeconds.Should().Be(0m); // default when not specified
    }

    [Fact]
    public void ExtractConfig_NoProperties_ReturnsDefaults()
    {
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", null, null, null);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(1.0m);
        config.PropertyPath.Should().Be("quantity");
        config.Unit.Should().BeEmpty();
        config.FrequencySeconds.Should().Be(60);
        config.SubjectName.Should().BeEmpty();
        config.StartDelaySeconds.Should().Be(0m);
    }

    [Fact]
    public void ExtractConfig_EmptyJsonObject_ReturnsDefaults()
    {
        var json = JsonDocument.Parse("{}").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", null, null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(1.0m);
        config.FrequencySeconds.Should().Be(60);
    }

    [Fact]
    public void ExtractConfig_PartialProperties_MergesWithDefaults()
    {
        var json = JsonDocument.Parse("""{"quantity": 10.0, "unit": "kWh"}""").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", "Solar", null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(10.0m);
        config.Unit.Should().Be("kWh");
        config.PropertyPath.Should().Be("quantity"); // default
        config.FrequencySeconds.Should().Be(60); // default
    }

    [Fact]
    public void ExtractConfig_StartDelaySeconds_ParsedFromProperties()
    {
        var json = JsonDocument.Parse("""{"quantity": 0.01, "startDelaySeconds": 15.5}""").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", "Home", null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.StartDelaySeconds.Should().Be(15.5m);
        config.Quantity.Should().Be(0.01m);
    }

    [Fact]
    public void ExtractConfig_StartDelaySeconds_StringType_Ignored()
    {
        var json = JsonDocument.Parse("""{"startDelaySeconds": "not-a-number"}""").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", null, null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.StartDelaySeconds.Should().Be(0m); // default — string ignored
    }

    [Fact]
    public void ExtractConfig_WrongPropertyTypes_IgnoresNonMatching()
    {
        // quantity as string should not be parsed (ValueKind != Number)
        var json = JsonDocument.Parse("""{"quantity": "not-a-number", "frequencySeconds": "fast"}""").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", null, null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(1.0m); // default, string ignored
        config.FrequencySeconds.Should().Be(60); // default, string ignored
    }

    [Fact]
    public void ExtractConfig_JsonArray_NotObject_ReturnsDefaults()
    {
        var json = JsonDocument.Parse("[1,2,3]").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", null, null, json);
        var config = HandleRequestProcessor.ExtractConfig(request);

        config.Quantity.Should().Be(1.0m); // Properties.Value.ValueKind != Object
    }

    #endregion

    #region ProcessHandle

    [Fact]
    public void ProcessHandle_ValidRequest_RegistersSimulation()
    {
        var json = JsonDocument.Parse("""{"quantity": 3.0}""").RootElement;
        var request = new HandleRequest("rel-1", "sub-1", "tgt-1", "Test", null, json);

        var (entry, error) = _processor.ProcessHandle(request);

        error.Should().BeNull();
        entry.Should().NotBeNull();
        entry!.Config.Quantity.Should().Be(3.0m);
        _engine.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void ProcessHandle_MissingSubjectId_ReturnsError()
    {
        var request = new HandleRequest("rel-1", "", "tgt-1", null, null, null);

        var (entry, error) = _processor.ProcessHandle(request);

        entry.Should().BeNull();
        error.Should().Contain("subjectId");
    }

    [Fact]
    public void ProcessHandle_MissingTargetId_ReturnsError()
    {
        var request = new HandleRequest("rel-1", "sub-1", "", null, null, null);

        var (entry, error) = _processor.ProcessHandle(request);

        entry.Should().BeNull();
        error.Should().Contain("targetId");
    }

    [Fact]
    public void ProcessHandle_NullSubjectId_ReturnsError()
    {
        var request = new HandleRequest("rel-1", null!, "tgt-1", null, null, null);

        var (entry, error) = _processor.ProcessHandle(request);

        entry.Should().BeNull();
        error.Should().NotBeNull();
    }

    #endregion
}
