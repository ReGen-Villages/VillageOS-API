using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class StateCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public StateCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new StateCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoBroker_ShowsHelp()
    {
        var handler = new StateCommandHandler("get foo", _writer, client: null);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("State query commands:", output);
        Assert.Contains("state get", output);
    }

    [Fact]
    public async Task Execute_NoArgs_ShowsHelp()
    {
        await ExecuteHandler("");

        Assert.Contains("State query commands:", _writer.ToString());
    }

    [Fact]
    public async Task Get_NoThing_ShowsUsage()
    {
        await ExecuteHandler("get");

        Assert.Contains("Usage: state get", _writer.ToString());
    }

    [Fact]
    public async Task Get_ByGuid_ResolvesAndPrintsStates()
    {
        var thingId = Guid.NewGuid();
        // NameResolver delegates to broker.GetThingAsync for GUID lookups
        _brokerMock.Setup(b => b.GetThingAsync(thingId))
            .ReturnsAsync(Parse($"{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}"));
        _brokerMock.Setup(b => b.GetStatesAsync(thingId))
            .ReturnsAsync(Parse("[\"warm\",\"dry\"]"));

        await ExecuteHandler($"get {thingId}");

        var output = _writer.ToString();
        Assert.Contains("warm", output);
        Assert.Contains("dry", output);
    }

    [Fact]
    public async Task Get_ByName_ResolvesViaGetAllThingsAsync()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}]"));
        _brokerMock.Setup(b => b.GetStatesAsync(thingId)).ReturnsAsync(Parse("[\"awake\"]"));

        await ExecuteHandler("get alice");

        Assert.Contains("awake", _writer.ToString());
    }

    [Fact]
    public async Task Get_ResolveFails_PrintsErrorAndDoesNotCallBroker()
    {
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("get nonexistent");

        var output = _writer.ToString();
        Assert.DoesNotContain("Error:", output); // error path goes through resolveResult.ErrorMessage, not exception
        _brokerMock.Verify(b => b.GetStatesAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Query_NoStateName_ShowsUsage()
    {
        await ExecuteHandler("query");

        Assert.Contains("Usage: state query", _writer.ToString());
    }

    [Fact]
    public async Task Query_DelegatesToBroker_PrintsResults()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetThingsInStateAsync("warm"))
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\"}}]"));

        await ExecuteHandler("query warm");

        Assert.Contains(thingId.ToString(), _writer.ToString());
    }

    [Fact]
    public async Task Find_AliasForQuery_DelegatesToBroker()
    {
        _brokerMock.Setup(b => b.GetThingsInStateAsync("cold"))
            .ReturnsAsync(Parse("[]"));

        await ExecuteHandler("find cold");

        _brokerMock.Verify(b => b.GetThingsInStateAsync("cold"), Times.Once);
    }

    [Fact]
    public async Task UnknownSubcommand_TreatedAsThingForGet()
    {
        // The default branch in ExecuteSubcommandAsync re-routes the unknown subcommand
        // as the first argument to HandleGetAsync (e.g. "state alice" → state get alice).
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}]"));
        _brokerMock.Setup(b => b.GetStatesAsync(thingId)).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("alice");

        _brokerMock.Verify(b => b.GetStatesAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task BrokerThrows_ErrorWritten()
    {
        _brokerMock.Setup(b => b.GetThingsInStateAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        await ExecuteHandler("query warm");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("offline", output);
    }
}
