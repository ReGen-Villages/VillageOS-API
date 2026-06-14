using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class PlantCommandHandlerTests : IDisposable
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;
    private readonly string _tempDir;

    public PlantCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
        _tempDir = Path.Combine(Path.GetTempPath(), $"PlantTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new PlantCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private string CreateSeedFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ========== Help Tests ==========

    [Fact]
    public async Task Execute_WithNoArguments_ShowsHelp()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage: plant <file> [mode] [options]", output);
        Assert.Contains("CurrentOnly", output);
        Assert.Contains("RingBuffer", output);
        Assert.Contains("FullHistory", output);
    }

    // ========== Basic Plant Tests ==========

    [Fact]
    public async Task Execute_WithValidFile_LoadsModel()
    {
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");

        await ExecuteHandler(seedPath);

        var output = _writer.ToString();
        Assert.Contains("Model loaded from", output);
        _myceliumMock.Verify(b => b.SetModelAsync(seedContent), Times.Once);
    }

    [Fact]
    public async Task Execute_WithFileWithoutExtension_AddsJsonExtension()
    {
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("model.json", seedContent);
        var pathWithoutExtension = Path.Combine(_tempDir, "model");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");

        await ExecuteHandler(pathWithoutExtension);

        var output = _writer.ToString();
        Assert.Contains("Model loaded from", output);
    }

    [Fact]
    public async Task Execute_WithNonexistentFile_ShowsError()
    {
        await ExecuteHandler("/nonexistent/path/model.json");

        var output = _writer.ToString();
        Assert.Contains("Error: File not found", output);
    }

    // ========== Plant with Mode Tests ==========

    [Fact]
    public async Task Execute_WithModeArgument_SetsAllPropertyModes()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":""test""}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""FullHistory""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", "FullHistory", null, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        Assert.Contains("Model loaded from", output);
        Assert.Contains("Setting all properties to FullHistory mode", output);
        Assert.Contains("Configured 1 properties across 1 things to FullHistory mode", output);
    }

    [Fact]
    public async Task Execute_WithRingBufferMode_SetsRingBufferSize()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""RingBuffer""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", "RingBuffer", 50, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} RingBuffer --ringbuffer=50");

        var output = _writer.ToString();
        Assert.Contains("Ring buffer size: 50", output);
        _myceliumMock.Verify(b => b.SetPropertyModeAsync(thingId, "Prop1", "RingBuffer", 50, null), Times.Once);
    }

    [Fact]
    public async Task Execute_WithSampledMode_SetsSampleRate()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""Sampled""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", "Sampled", null, 25))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} Sampled --samplerate=25");

        var output = _writer.ToString();
        Assert.Contains("Sample rate: 1 in 25", output);
        _myceliumMock.Verify(b => b.SetPropertyModeAsync(thingId, "Prop1", "Sampled", null, 25), Times.Once);
    }

    // ========== Multiple Properties Tests ==========

    [Fact]
    public async Task Execute_WithMultipleThingsAndProperties_SetsAllModes()
    {
        var thingId1 = Guid.NewGuid();
        var thingId2 = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[
            {{""Id"":""{thingId1}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}},""Prop2"":{{""Value"":2}}}}}},
            {{""Id"":""{thingId2}"",""Name"":""Thing2"",""Properties"":{{""PropA"":{{""Value"":""a""}}}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""CurrentOnly""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(It.IsAny<Guid>(), It.IsAny<string>(), "CurrentOnly", null, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} CurrentOnly");

        var output = _writer.ToString();
        Assert.Contains("Configured 3 properties across 2 things to CurrentOnly mode", output);
    }

    // ========== Mode Case Insensitivity Tests ==========

    [Theory]
    [InlineData("fullhistory")]
    [InlineData("FULLHISTORY")]
    [InlineData("FullHistory")]
    public async Task Execute_ModeIsCaseInsensitive(string mode)
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile($"test_{mode}.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""FullHistory""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", mode, null, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} {mode}");

        var output = _writer.ToString();
        Assert.Contains($"Setting all properties to {mode} mode", output);
    }

    // ========== Error Handling Tests ==========

    [Fact]
    public async Task Execute_WhenMyceliumThrows_ShowsError()
    {
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await ExecuteHandler(seedPath);

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("Connection refused", output);
    }

    [Fact]
    public async Task Execute_WhenPropertyModeSetFails_ShowsWarningAndContinues()
    {
        var thingId1 = Guid.NewGuid();
        var thingId2 = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[
            {{""Id"":""{thingId1}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}},
            {{""Id"":""{thingId2}"",""Name"":""Thing2"",""Properties"":{{""PropA"":{{""Value"":""a""}}}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""FullHistory""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId1, "Prop1", "FullHistory", null, null))
            .ThrowsAsync(new Exception("Property not found"));
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId2, "PropA", "FullHistory", null, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        Assert.Contains("Warning: Could not set mode for Thing1.Prop1", output);
        Assert.Contains("Configured 1 properties across 1 things to FullHistory mode", output);
    }

    // ========== SetAllPropertyModesAsync Edge Cases ==========

    [Fact]
    public async Task Execute_WithThingMissingId_SkipsThing()
    {
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        // Thing without Id field
        var thingsJson = @"[{""Name"":""NoIdThing"",""Properties"":{""Prop1"":{""Value"":1}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        // Should report 0 properties since thing has no Id
        Assert.Contains("Configured 0 properties across 0 things", output);
    }

    [Fact]
    public async Task Execute_WithNonObjectProperties_SkipsProperties()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        // Properties is not an object (it's an array instead)
        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":[]}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        // Should report 0 properties since Properties is not an object
        Assert.Contains("Configured 0 properties across 0 things", output);
    }

    [Fact]
    public async Task Execute_WithThingWithNoProperties_NotCounted()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        // Thing with empty properties object
        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""EmptyThing"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        // Should report 0 things counted since thingPropertyCount is 0
        Assert.Contains("Configured 0 properties across 0 things", output);
    }

    [Fact]
    public async Task Execute_WithMixedThings_CountsCorrectly()
    {
        var thing1Id = Guid.NewGuid();
        var thing2Id = Guid.NewGuid();
        var thing3Id = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        // Mix: one with properties, one without, one without Properties field
        var thingsJson = $@"[
            {{""Id"":""{thing1Id}"",""Name"":""WithProps"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}},
            {{""Id"":""{thing2Id}"",""Name"":""EmptyProps"",""Properties"":{{}}}},
            {{""Id"":""{thing3Id}"",""Name"":""NoPropsField""}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""FullHistory""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thing1Id, "Prop1", "FullHistory", null, null))
            .ReturnsAsync(modeResult);

        await ExecuteHandler($"{seedPath} FullHistory");

        var output = _writer.ToString();
        // Only Thing1 has properties, so 1 property across 1 thing
        Assert.Contains("Configured 1 properties across 1 things", output);
    }

    [Fact]
    public async Task Execute_WithInvalidMode_OnlyLoadsFile()
    {
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");

        await ExecuteHandler($"{seedPath} InvalidMode");

        var output = _writer.ToString();
        Assert.Contains("Model loaded from", output);
        Assert.DoesNotContain("Setting all properties", output);
    }

    // ========== ParseArguments Edge Cases ==========

    [Fact]
    public async Task Execute_WithMultipleOptions_ParsesCorrectly()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""RingBuffer""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", "RingBuffer", 50, null))
            .ReturnsAsync(modeResult);

        // Args with mode and option
        await ExecuteHandler($"{seedPath} --ringbuffer=50 RingBuffer");

        var output = _writer.ToString();
        Assert.Contains("Setting all properties to RingBuffer mode", output);
        Assert.Contains("Ring buffer size: 50", output);
    }

    [Fact]
    public async Task Execute_WithCaseInsensitiveOptions_Parses()
    {
        var thingId = Guid.NewGuid();
        var seedContent = @"{""Id"":""00000000-0000-0000-0000-000000000001"",""Name"":""Test"",""Things"":[]}";
        var seedPath = CreateSeedFile("test.json", seedContent);

        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""Thing1"",""Properties"":{{""Prop1"":{{""Value"":1}}}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var modeResult = JsonSerializer.Deserialize<JsonElement>(@"{""Mode"":""Sampled""}");

        _myceliumMock.Setup(b => b.SetModelAsync(seedContent)).ReturnsAsync("OK");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.SetPropertyModeAsync(thingId, "Prop1", "Sampled", null, 10))
            .ReturnsAsync(modeResult);

        // Use uppercase option prefix
        await ExecuteHandler($"{seedPath} Sampled --SAMPLERATE=10");

        var output = _writer.ToString();
        Assert.Contains("Sample rate: 1 in 10", output);
    }

    // ========== Cleanup ==========

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }
}
