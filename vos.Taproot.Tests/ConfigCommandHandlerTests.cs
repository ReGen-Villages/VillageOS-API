using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class ConfigCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public ConfigCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new ConfigCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    // ========== ExecuteAsync Tests ==========

    [Fact]
    public async Task Execute_WithNoArguments_ShowsHelp()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Config commands:", output);
    }

    [Fact]
    public async Task Execute_WithHelpCommand_ShowsHelp()
    {
        await ExecuteHandler("help");

        var output = _writer.ToString();
        Assert.Contains("Config commands:", output);
    }

    [Fact]
    public async Task Execute_WithUnknownCommand_ShowsHelp()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Config commands:", output);
    }

    // ========== Command Dispatch Tests ==========

    [Fact]
    public async Task Execute_ModeCommand_DispatchesToPropertyModeHandler()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    [Fact]
    public async Task Execute_PropertyModeCommand_DispatchesToPropertyModeHandler()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("property-mode");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    [Fact]
    public async Task Execute_ModeCommand_CaseInsensitive()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("MODE");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    // ========== HandlePropertyModeAsync Tests ==========

    [Fact]
    public async Task Mode_WithNoArgs_ShowsCurrentConfig()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""RingBuffer"",""RingBufferSize"":200,""SampleRate"":50}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode");

        var output = _writer.ToString();
        Assert.Contains("Default Mode:     RingBuffer", output);
        Assert.Contains("Ring Buffer Size: 200", output);
        Assert.Contains("Sample Rate:      50", output);
        Assert.Contains("Available modes:", output);
    }

    [Fact]
    public async Task Mode_GetWithNoArgs_ShowsDefaultMode()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""FullHistory"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode get");

        var output = _writer.ToString();
        Assert.Contains("Default property mode: FullHistory", output);
    }

    [Fact]
    public async Task Mode_GetWithInsufficientArgs_ShowsUsage()
    {
        await ExecuteHandler("mode get MyThing");

        var output = _writer.ToString();
        Assert.Contains("Usage: config mode get <thing> <propertyName>", output);
    }

    [Fact]
    public async Task Mode_GetWithThingAndProperty_ShowsPropertyMode()
    {
        var thingId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""MyThing"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var propMode = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""RingBuffer"",""RingBufferCapacity"":100,""RingBufferCount"":5}");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.GetPropertyModeAsync(thingId, "Status")).ReturnsAsync(propMode);

        await ExecuteHandler("mode get MyThing Status");

        var output = _writer.ToString();
        Assert.Contains("Property: Status", output);
        Assert.Contains("Mode: RingBuffer", output);
        Assert.Contains("Ring Buffer Capacity: 100", output);
        Assert.Contains("Ring Buffer Count: 5", output);
    }

    [Fact]
    public async Task Mode_GetWithInvalidThing_ShowsError()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);

        await ExecuteHandler("mode get NonExistent Status");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
    }

    // ========== HandleSetPropertyModeAsync Tests ==========

    [Fact]
    public async Task Mode_SetWithNoArgs_ShowsUsage()
    {
        await ExecuteHandler("mode set");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("config mode set", output);
    }

    [Fact]
    public async Task Mode_SetWithModeName_SetsDefaultMode()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""Sampled"",""RingBufferSize"":100,""SampleRate"":50}");
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("Sampled", null, null)).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode set Sampled");

        var output = _writer.ToString();
        Assert.Contains("Default property mode set to: Sampled", output);
    }

    // ========== Valid Mode Name Direct Set Tests ==========

    [Fact]
    public async Task Mode_WithValidModeName_SetsDefaultDirectly()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""DefaultMode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        // The mode name gets lowercased in HandlePropertyModeAsync before being passed to SetDefaultModeAsync
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("currentonly", null, null)).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode CurrentOnly");

        var output = _writer.ToString();
        Assert.Contains("Default property mode set to: CurrentOnly", output);
    }

    [Fact]
    public async Task Mode_WithUnknownAction_ShowsError()
    {
        await ExecuteHandler("mode invalidaction");

        var output = _writer.ToString();
        Assert.Contains("Unknown property-mode subcommand: invalidaction", output);
        Assert.Contains("Use: config mode [get|set]", output);
    }

    // ========== Error Handling Tests ==========

    [Fact]
    public async Task Execute_WhenExceptionThrown_ShowsError()
    {
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await ExecuteHandler("mode");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("Connection refused", output);
    }

    // ========== Named-arg parsing ==========

    [Fact]
    public async Task Execute_DefaultModeRingBufferWithSize_ParsesNamedArg()
    {
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("ringbuffer", 50, null))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler("mode ringbuffer --ringbuffer=50");

        _myceliumMock.Verify(b => b.SetDefaultPropertyModeAsync("ringbuffer", 50, null), Times.Once);
    }

    [Fact]
    public async Task Execute_DefaultModeSampledWithRate_ParsesNamedArg()
    {
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("sampled", null, 100))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler("mode sampled --samplerate=100");

        _myceliumMock.Verify(b => b.SetDefaultPropertyModeAsync("sampled", null, 100), Times.Once);
    }
}
