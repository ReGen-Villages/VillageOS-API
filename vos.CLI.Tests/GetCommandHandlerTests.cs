using System.Text.Json;
using vos.CLI;
using Moq;
using Xunit;

namespace vos.CLI.Tests;

public class GetCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public GetCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new GetCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Get_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("get thing", output);
    }

    [Fact]
    public async Task Get_UnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task GetThing_NoId_ShowsUsage()
    {
        await ExecuteHandler("thing");

        Assert.Contains("Usage: get thing", _writer.ToString());
    }

    [Fact]
    public async Task GetThing_InvalidNameOrGuid_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        await ExecuteHandler("thing invalid-name");

        Assert.Contains("Error", _writer.ToString());
    }

    [Fact]
    public async Task GetThing_WithName_ReturnsJson()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var thingJson = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}");
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);

        await ExecuteHandler("thing MyThing");

        _brokerMock.Verify(b => b.GetThingAsync(thingId), Times.Once);
        Assert.Contains("MyThing", _writer.ToString());
    }

    [Fact]
    public async Task GetThing_WhenExists_OutputsJson()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"id\":\"{thingId}\",\"name\":\"TestThing\"}}");
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(json);

        await ExecuteHandler($"thing {thingId}");

        _brokerMock.Verify(b => b.GetThingAsync(thingId), Times.Once);
        Assert.Contains("TestThing", _writer.ToString());
    }

    [Fact]
    public async Task GetThing_WhenNotFound_ShowsNotFoundMessage()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync((JsonElement?)null);

        await ExecuteHandler($"thing {thingId}");

        Assert.Contains($"Thing {thingId} not found", _writer.ToString());
    }

    [Fact]
    public async Task GetRelationship_ShowsNotAvailableMessage()
    {
        await ExecuteHandler("relationship");

        Assert.Contains("not available in remote mode", _writer.ToString());
    }
}
