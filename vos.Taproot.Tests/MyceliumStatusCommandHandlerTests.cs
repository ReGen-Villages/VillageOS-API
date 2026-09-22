using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class MyceliumStatusCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public MyceliumStatusCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new MyceliumStatusCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("mycelium status", output);
        Assert.Contains("mycelium endpoints", output);
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
    public async Task Status_DelegatesToMycelium_AndPrintsJson()
    {
        _myceliumMock.Setup(b => b.GetSeedStatusAsync())
            .ReturnsAsync(Parse("{\"loaded\":true,\"count\":3}"));

        await ExecuteHandler("status");

        var output = _writer.ToString();
        Assert.Contains("loaded", output);
        Assert.Contains("count", output);
    }

    [Fact]
    public async Task Endpoints_NoneRegistered_ShowsFriendlyMessage()
    {
        _myceliumMock.Setup(b => b.GetEndpointsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("endpoints");

        Assert.Contains("No endpoint services registered.", _writer.ToString());
    }

    [Fact]
    public async Task Endpoints_NotAnArray_ShowsFriendlyMessage()
    {
        _myceliumMock.Setup(b => b.GetEndpointsAsync()).ReturnsAsync(Parse("{}"));

        await ExecuteHandler("endpoints");

        Assert.Contains("No endpoint services registered.", _writer.ToString());
    }

    [Fact]
    public async Task Endpoints_WithItems_ListsEachWithSubdomainAndUrl()
    {
        var json = "[{\"Subdomain\":\"echo\",\"BaseUrl\":\"https://echo.local\"},{\"Subdomain\":\"meta\",\"BaseUrl\":\"https://meta.local\"}]";
        _myceliumMock.Setup(b => b.GetEndpointsAsync()).ReturnsAsync(Parse(json));

        await ExecuteHandler("endpoints");

        var output = _writer.ToString();
        Assert.Contains("Endpoint services:", output);
        Assert.Contains("echo", output);
        Assert.Contains("https://echo.local", output);
        Assert.Contains("meta", output);
    }

    [Fact]
    public async Task Endpoints_MissingFields_FallsBackToUnknown()
    {
        // Endpoint with neither Subdomain nor BaseUrl exercises the GetStringOrDefault default branch.
        _myceliumMock.Setup(b => b.GetEndpointsAsync()).ReturnsAsync(Parse("[{}]"));

        await ExecuteHandler("endpoints");

        Assert.Contains("unknown", _writer.ToString());
    }

    [Fact]
    public async Task MyceliumThrows_ErrorWritten()
    {
        _myceliumMock.Setup(b => b.GetSeedStatusAsync()).ThrowsAsync(new HttpRequestException("offline"));

        await ExecuteHandler("status");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("offline", output);
    }
}
