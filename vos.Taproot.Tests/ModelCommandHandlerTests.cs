using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class ModelCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public ModelCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new ModelCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("model list", output);
        Assert.Contains("model switch", output);
    }

    [Fact]
    public async Task Execute_NullArg_ShowsUsage()
    {
        await ExecuteHandler(null!);

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Execute_UnknownSubcommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Switch_NoArg_ShowsUsage()
    {
        // "switch" alone (without modelId/name) doesn't match the `when tok.Length >= 2` pattern.
        await ExecuteHandler("switch");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task List_EmptyArray_ShowsNoModelsMessage()
    {
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("list");

        Assert.Contains("No models found.", _writer.ToString());
    }

    [Fact]
    public async Task List_NotAnArray_ShowsNoModelsMessage()
    {
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("list");

        Assert.Contains("No models found.", _writer.ToString());
    }

    [Fact]
    public async Task List_WithModels_ListsEach()
    {
        var json = "[{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"Estate\"},{\"Id\":\"00000000-0000-0000-0000-000000000002\",\"Name\":\"Demo\"}]";
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse(json));

        await ExecuteHandler("list");

        var output = _writer.ToString();
        Assert.Contains("Available models:", output);
        Assert.Contains("Estate", output);
        Assert.Contains("Demo", output);
    }

    [Fact]
    public async Task Switch_ValidGuid_DelegatesDirectly()
    {
        var modelId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.SwitchModelAsync(modelId)).ReturnsAsync(Parse("{}"));

        await ExecuteHandler($"switch {modelId}");

        var output = _writer.ToString();
        Assert.Contains("Switched to model", output);
        Assert.Contains(modelId.ToString(), output);
        _myceliumMock.Verify(b => b.SwitchModelAsync(modelId), Times.Once);
    }

    [Fact]
    public async Task Switch_NameMatch_LooksUpAndSwitches()
    {
        var modelId = Guid.NewGuid();
        var json = $"[{{\"Id\":\"{modelId}\",\"Name\":\"Estate\"}}]";
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse(json));
        _myceliumMock.Setup(b => b.SwitchModelAsync(modelId)).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("switch estate"); // case-insensitive

        Assert.Contains("Switched to model", _writer.ToString());
        _myceliumMock.Verify(b => b.SwitchModelAsync(modelId), Times.Once);
    }

    [Fact]
    public async Task Switch_NameNotFound_PrintsNotFound()
    {
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("switch ghost");

        Assert.Contains("Model not found:", _writer.ToString());
        _myceliumMock.Verify(b => b.SwitchModelAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Switch_NameWithMissingId_NotFound()
    {
        // Model in list has a Name match but a missing/invalid Id → FindModelByName returns null
        var json = "[{\"Name\":\"NoId\"}]";
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse(json));

        await ExecuteHandler("switch NoId");

        Assert.Contains("Model not found:", _writer.ToString());
    }

    [Fact]
    public async Task Switch_NameLookupFromNonArrayModels_NotFound()
    {
        _myceliumMock.Setup(b => b.ListModelsAsync()).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("switch anything");

        Assert.Contains("Model not found:", _writer.ToString());
    }

    [Fact]
    public async Task MyceliumThrows_ErrorWritten()
    {
        _myceliumMock.Setup(b => b.ListModelsAsync()).ThrowsAsync(new HttpRequestException("offline"));

        await ExecuteHandler("list");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("offline", output);
    }
}
