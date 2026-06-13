using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class StartCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public StartCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new StartCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    // ========== ExecuteAsync Tests ==========

    [Fact]
    public async Task Execute_WithNoArguments_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("start service", output);
    }

    [Fact]
    public async Task Execute_WithUnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    // ========== StartServiceAsync Tests ==========

    [Fact]
    public async Task StartService_WithNoHandler_ShowsUsage()
    {
        await ExecuteHandler("service");

        var output = _writer.ToString();
        Assert.Contains("Usage: start service", output);
    }

    [Fact]
    public async Task StartService_WithValidName_ResolvesAndStarts()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("service MyHandler");

        var output = _writer.ToString();
        Assert.Contains("started successfully", output);
    }

    [Fact]
    public async Task StartService_WithValidGuid_DirectlyStarts()
    {
        var handlerId = Guid.NewGuid();
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler($"service {handlerId}");

        var output = _writer.ToString();
        Assert.Contains("started successfully", output);
    }

    [Fact]
    public async Task StartService_WhenStartFails_ShowsFailureMessage()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(false);

        await ExecuteHandler("service MyHandler");

        var output = _writer.ToString();
        Assert.Contains("Failed to start service", output);
    }

    [Fact]
    public async Task StartService_WithInvalidName_ShowsError()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);

        await ExecuteHandler("service NonExistent");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
    }

    // ========== Command Dispatch Tests ==========

    [Fact]
    public async Task Execute_ServiceCommand_CaseInsensitive()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("SERVICE MyHandler");

        var output = _writer.ToString();
        Assert.Contains("started successfully", output);
    }

    // ========== ShowGuids Flag Tests ==========

    [Fact]
    public async Task StartService_WithShowGuidsFlag_ShowsGuidInOutput()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("service MyHandler --showguids");

        var output = _writer.ToString();
        Assert.Contains(handlerId.ToString(), output);
    }

    [Fact]
    public async Task StartService_WithoutShowGuidsFlag_HidesGuid()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(handlerId)).ReturnsAsync(true);

        await ExecuteHandler("service MyHandler");

        var output = _writer.ToString();
        Assert.DoesNotContain(handlerId.ToString(), output);
    }

    // ========== Error Handling Tests ==========

    [Fact]
    public async Task StartService_WhenExceptionThrown_ShowsError()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{handlerId}"",""Name"":""MyHandler"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _brokerMock.Setup(b => b.StartServiceAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await ExecuteHandler("service MyHandler");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("Connection refused", output);
    }
}
