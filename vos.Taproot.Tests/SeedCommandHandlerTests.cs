using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class SeedCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public SeedCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new SeedCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("seed status", output);
        Assert.Contains("seed list", output);
        Assert.Contains("seed load", output);
        Assert.Contains("seed save", output);
        Assert.Contains("seed reload", output);
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
    public async Task Status_DelegatesToBroker_AndPrintsJson()
    {
        _brokerMock.Setup(b => b.GetSeedStatusAsync()).ReturnsAsync(Parse("{\"loaded\":2}"));

        await ExecuteHandler("status");

        Assert.Contains("loaded", _writer.ToString());
    }

    [Fact]
    public async Task List_NoSeeds_ShowsFriendlyMessage()
    {
        _brokerMock.Setup(b => b.ListLibrarySeedsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("list");

        Assert.Contains("No library seeds found.", _writer.ToString());
    }

    [Fact]
    public async Task List_NotAnArray_ShowsFriendlyMessage()
    {
        _brokerMock.Setup(b => b.ListLibrarySeedsAsync()).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("list");

        Assert.Contains("No library seeds found.", _writer.ToString());
    }

    [Fact]
    public async Task List_WithSeeds_PrintsName()
    {
        _brokerMock.Setup(b => b.ListLibrarySeedsAsync())
            .ReturnsAsync(Parse("[{\"Name\":\"forest\"},{\"Name\":\"village\"}]"));

        await ExecuteHandler("list");

        var output = _writer.ToString();
        Assert.Contains("Library seeds:", output);
        Assert.Contains("forest", output);
        Assert.Contains("village", output);
    }

    // Note: the production fallback `seed.GetStringOrDefault("Name", seed.ToString())` throws
    // on a non-object seed because TryGetProperty rejects string elements. The outer try-catch
    // converts the throw into "Error: ..." output — a latent bug worth tracking separately.
    // Happy path (object seeds with Name) is exercised in List_WithSeeds_PrintsName above.

    [Fact]
    public async Task Load_NoArg_ShowsUsage()
    {
        await ExecuteHandler("load");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Load_DelegatesToBroker_PrintsConfirmation()
    {
        _brokerMock.Setup(b => b.LoadLibrarySeedAsync("forest")).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("load forest");

        var output = _writer.ToString();
        Assert.Contains("Loading seed 'forest'...", output);
        Assert.Contains("Seed 'forest' loaded.", output);
        _brokerMock.Verify(b => b.LoadLibrarySeedAsync("forest"), Times.Once);
    }

    [Fact]
    public async Task Save_NoArg_ShowsUsage()
    {
        await ExecuteHandler("save");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Save_DelegatesToBroker_PrintsConfirmation()
    {
        _brokerMock.Setup(b => b.SaveLibrarySeedAsync("village")).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("save village");

        var output = _writer.ToString();
        Assert.Contains("Saving current model as seed 'village'...", output);
        Assert.Contains("Model saved as seed 'village'.", output);
        _brokerMock.Verify(b => b.SaveLibrarySeedAsync("village"), Times.Once);
    }

    [Fact]
    public async Task Reload_DelegatesToBroker_PrintsConfirmation()
    {
        _brokerMock.Setup(b => b.ReloadSeedsAsync()).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("reload");

        var output = _writer.ToString();
        Assert.Contains("Reloading seeds from disk...", output);
        Assert.Contains("Seeds reloaded.", output);
    }

    [Fact]
    public async Task BrokerThrows_ErrorWritten()
    {
        _brokerMock.Setup(b => b.GetSeedStatusAsync()).ThrowsAsync(new HttpRequestException("offline"));

        await ExecuteHandler("status");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("offline", output);
    }
}
