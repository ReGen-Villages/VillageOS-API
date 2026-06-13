using System.Text.Json;
using vos.Taproot;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class SetCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public SetCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    [Fact]
    public async Task Set_ValidStringProperty_SetsValue()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue")).ReturnsAsync(json);

        var handler = new SetCommandHandler($"{thingId} MyProp TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Set MyProp = TestValue", output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task Set_InsufficientArgs_ShowsUsage()
    {
        var handler = new SetCommandHandler("onlyOneArg", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Set_TwoArgs_ShowsUsage()
    {
        var id = Guid.NewGuid();
        var handler = new SetCommandHandler($"{id} PropName", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Set_InvalidNameOrGuid_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new SetCommandHandler("not-a-thing PropName Value", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Error:", _writer.ToString());
    }

    [Fact]
    public async Task Set_WithName_SetsValue()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue")).ReturnsAsync(json);

        var handler = new SetCommandHandler("MyThing MyProp TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Set MyProp = TestValue", output);
        Assert.Contains("MyThing", output);
    }

    // --showguids flag tests

    [Fact]
    public async Task Set_WithShowGuidsFlag_ShowsGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue")).ReturnsAsync(json);

        var handler = new SetCommandHandler("MyThing MyProp TestValue --showguids", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Set MyProp = TestValue", output);
        Assert.Contains("MyThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task Set_WithoutShowGuidsFlag_HidesGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "MyProp", "string", "TestValue")).ReturnsAsync(json);

        var handler = new SetCommandHandler("MyThing MyProp TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Set MyProp = TestValue", output);
        Assert.Contains("MyThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }
}
