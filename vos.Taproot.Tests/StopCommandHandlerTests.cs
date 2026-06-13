using System.Text.Json;
using vos.Taproot;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class StopCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public StopCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new StopCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Stop_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("stop service", output);
    }

    [Fact]
    public async Task Stop_UnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task StopService_NoId_ShowsUsage()
    {
        await ExecuteHandler("service");

        Assert.Contains("Usage: stop service", _writer.ToString());
    }

    [Fact]
    public async Task StopService_InvalidName_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        await ExecuteHandler("service invalid-name");

        Assert.Contains("Error", _writer.ToString());
    }

    [Fact]
    public async Task StopService_WhenExists_StopsAndShowsMessage()
    {
        var handlerId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.StopServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler($"service {handlerId}");

        _brokerMock.Verify(b => b.StopServiceAsync(handlerId), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Stop request sent to service", output);
        Assert.Contains("MyHandler", output);
    }

    [Fact]
    public async Task StopService_WithName_StopsAndShowsMessage()
    {
        var handlerId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.StopServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("service MyHandler");

        _brokerMock.Verify(b => b.StopServiceAsync(handlerId), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Stop request sent to service", output);
        Assert.Contains("MyHandler", output);
    }

    [Fact]
    public async Task StopService_WhenNotFound_ShowsNotFoundMessage()
    {
        var handlerId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.StopServiceAsync(handlerId)).ReturnsAsync(false);

        await ExecuteHandler($"service {handlerId}");

        var output = _writer.ToString();
        Assert.Contains("Service", output);
        Assert.Contains("not found", output);
    }

    // --showguids flag tests

    [Fact]
    public async Task StopService_WithShowGuidsFlag_ShowsGuid()
    {
        var handlerId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.StopServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler($"service MyHandler --showguids");

        var output = _writer.ToString();
        Assert.Contains("Stop request sent to service", output);
        Assert.Contains("MyHandler", output);
        Assert.Contains(handlerId.ToString(), output);
    }

    [Fact]
    public async Task StopService_WithoutShowGuidsFlag_HidesGuid()
    {
        var handlerId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.StopServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("service MyHandler");

        var output = _writer.ToString();
        Assert.Contains("Stop request sent to service", output);
        Assert.Contains("MyHandler", output);
        Assert.DoesNotContain(handlerId.ToString(), output);
    }

    // Daemon tests

    [Fact]
    public async Task StopDaemon_NoKey_ShowsUsage()
    {
        await ExecuteHandler("daemon");

        var output = _writer.ToString();
        Assert.Contains("Usage: stop daemon", output);
        Assert.Contains("list daemons", output);
    }

    [Fact]
    public async Task StopDaemon_WhenExists_StopsAndShowsMessage()
    {
        _brokerMock.Setup(b => b.StopDaemonAsync("is:5100")).ReturnsAsync(true);

        await ExecuteHandler("daemon is:5100");

        _brokerMock.Verify(b => b.StopDaemonAsync("is:5100"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Daemon 'is:5100' stopped", output);
    }

    [Fact]
    public async Task StopDaemon_WhenNotFound_ShowsNotFoundMessage()
    {
        _brokerMock.Setup(b => b.StopDaemonAsync("nonexistent:1234")).ReturnsAsync(false);

        await ExecuteHandler("daemon nonexistent:1234");

        var output = _writer.ToString();
        Assert.Contains("not found or not running", output);
    }
}
