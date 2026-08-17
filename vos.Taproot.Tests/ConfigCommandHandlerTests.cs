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
            @"{""Mode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    [Fact]
    public async Task Execute_PropertyModeCommand_DispatchesToPropertyModeHandler()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("property-mode");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    [Fact]
    public async Task Execute_ModeCommand_CaseInsensitive()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("MODE");

        var output = _writer.ToString();
        Assert.Contains("Property Mode Configuration:", output);
    }

    // ========== HandlePropertyModeAsync Tests ==========

    // The fixtures below are the reply the platform actually sends: a field named Mode, and the modes
    // it accepts. Reading a field it does not send printed an empty default and nothing failed (#6517).
    [Fact]
    public async Task Mode_WithNoArgs_ShowsCurrentConfigAndTheModesThePlatformReports()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""RingBuffer"",""RingBufferSize"":200,""SampleRate"":50,
               ""AvailableModes"":[""CurrentOnly"",""RingBuffer"",""Sampled"",""FullHistory""]}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode");

        var output = _writer.ToString();
        Assert.Contains("Default Mode:     RingBuffer", output);
        Assert.Contains("Ring Buffer Size: 200", output);
        Assert.Contains("Sample Rate:      50", output);
        Assert.Contains("Available modes: CurrentOnly, RingBuffer, Sampled, FullHistory", output);
    }

    // Regression (#6513): the client used to print a list of modes it was built with, which could
    // disagree with the platform's.
    [Fact]
    public async Task Mode_WithNoArgs_ReportsAModeThisClientWasNeverBuiltWith()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100,
               ""AvailableModes"":[""CurrentOnly"",""EveryOtherChange""]}");
        _myceliumMock.Setup(b => b.GetDefaultPropertyModeAsync()).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode");

        Assert.Contains("Available modes: CurrentOnly, EveryOtherChange", _writer.ToString());
    }

    [Fact]
    public async Task Mode_GetWithNoArgs_ShowsDefaultMode()
    {
        var modeConfig = JsonSerializer.Deserialize<JsonElement>(
            @"{""Mode"":""FullHistory"",""RingBufferSize"":100,""SampleRate"":100}");
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
            @"{""Mode"":""Sampled"",""RingBufferSize"":100,""SampleRate"":50}");
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
            @"{""Mode"":""CurrentOnly"",""RingBufferSize"":100,""SampleRate"":100}");
        // The mode name gets lowercased in HandlePropertyModeAsync before being passed to SetDefaultModeAsync
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("currentonly", null, null)).ReturnsAsync(modeConfig);

        await ExecuteHandler("mode CurrentOnly");

        var output = _writer.ToString();
        Assert.Contains("Default property mode set to: CurrentOnly", output);
    }

    // Regression (#6513): a word that is not a subcommand is a mode name, and the platform decides
    // whether it is one. The client used to refuse it first, from a list of its own.
    [Fact]
    public async Task Mode_WithAWordThatIsNotASubcommand_SendsItAsAModeAndShowsThePlatformsRefusal()
    {
        _myceliumMock.Setup(b => b.SetDefaultPropertyModeAsync("everyotherchange", null, null))
            .ThrowsAsync(new HttpRequestException(
                """400 Bad Request: {"error":"Invalid mode: everyotherchange","availableModes":["CurrentOnly","RingBuffer","Sampled","FullHistory"]}"""));

        await ExecuteHandler("mode everyotherchange");

        var output = _writer.ToString();
        Assert.Contains("Invalid mode: everyotherchange", output);
        Assert.Contains("CurrentOnly", output);
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
