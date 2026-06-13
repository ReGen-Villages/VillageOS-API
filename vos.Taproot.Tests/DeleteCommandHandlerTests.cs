using System.Text.Json;
using vos.Taproot;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class DeleteCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public DeleteCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new DeleteCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Delete_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("delete thing", output);
        Assert.Contains("delete relationship", output);
        Assert.Contains("delete property", output);
    }

    [Fact]
    public async Task Delete_UnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task DeleteThing_NoId_ShowsUsage()
    {
        await ExecuteHandler("thing");

        Assert.Contains("Usage: delete thing", _writer.ToString());
    }

    [Fact]
    public async Task DeleteThing_InvalidNameOrGuid_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        await ExecuteHandler("thing invalid-name");

        Assert.Contains("Error", _writer.ToString());
        Assert.Contains("No thing found", _writer.ToString());
    }

    [Fact]
    public async Task DeleteThing_WithName_Deletes()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(true);

        await ExecuteHandler("thing MyThing");

        _brokerMock.Verify(b => b.DeleteThingAsync(thingId), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted thing", output);
        Assert.Contains("MyThing", output);
    }

    [Fact]
    public async Task DeleteThing_WhenExists_DeletesAndShowsMessage()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(true);

        await ExecuteHandler($"thing {thingId}");

        _brokerMock.Verify(b => b.DeleteThingAsync(thingId), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted thing", output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task DeleteThing_WhenNotFound_ShowsNotFoundMessage()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(false);

        await ExecuteHandler($"thing {thingId}");

        var output = _writer.ToString();
        Assert.Contains("Thing", output);
        Assert.Contains("not found", output);
    }

    [Fact]
    public async Task DeleteRelationship_NoId_ShowsUsage()
    {
        await ExecuteHandler("relationship");

        Assert.Contains("Usage: delete relationship <id>", _writer.ToString());
    }

    [Fact]
    public async Task DeleteRelationship_InvalidGuid_ShowsError()
    {
        await ExecuteHandler("relationship invalid-guid");

        Assert.Contains("Error: Invalid GUID format", _writer.ToString());
    }

    [Fact]
    public async Task DeleteRelationship_WhenExists_DeletesAndShowsMessage()
    {
        var relationshipId = Guid.NewGuid();
        _brokerMock.Setup(b => b.DeleteRelationshipAsync(relationshipId)).ReturnsAsync(true);

        await ExecuteHandler($"relationship {relationshipId}");

        _brokerMock.Verify(b => b.DeleteRelationshipAsync(relationshipId), Times.Once);
        Assert.Contains("Deleted relationship", _writer.ToString());
    }

    [Fact]
    public async Task DeleteRelationship_WhenNotFound_ShowsNotFoundMessage()
    {
        var relationshipId = Guid.NewGuid();
        _brokerMock.Setup(b => b.DeleteRelationshipAsync(relationshipId)).ReturnsAsync(false);

        await ExecuteHandler($"relationship {relationshipId}");

        Assert.Contains($"Relationship {relationshipId} not found", _writer.ToString());
    }

    [Fact]
    public async Task DeleteRelation_Alias_Works()
    {
        var relationshipId = Guid.NewGuid();
        _brokerMock.Setup(b => b.DeleteRelationshipAsync(relationshipId)).ReturnsAsync(true);

        await ExecuteHandler($"relation {relationshipId}");

        _brokerMock.Verify(b => b.DeleteRelationshipAsync(relationshipId), Times.Once);
        Assert.Contains("Deleted relationship", _writer.ToString());
    }

    [Fact]
    public async Task DeleteProperty_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("property");

        Assert.Contains("Usage: delete property", _writer.ToString());
    }

    [Fact]
    public async Task DeleteProperty_OnlyThingId_ShowsUsage()
    {
        var thingId = Guid.NewGuid();
        await ExecuteHandler($"property {thingId}");

        Assert.Contains("Usage: delete property", _writer.ToString());
    }

    [Fact]
    public async Task DeleteProperty_InvalidNameOrGuid_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        await ExecuteHandler("property invalid-name propName");

        Assert.Contains("Error", _writer.ToString());
    }

    [Fact]
    public async Task DeleteProperty_WithName_Deletes()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeletePropertyAsync(thingId, "testProp")).ReturnsAsync(true);

        await ExecuteHandler("property MyThing testProp");

        _brokerMock.Verify(b => b.DeletePropertyAsync(thingId, "testProp"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted property 'testProp' from thing", output);
        Assert.Contains("MyThing", output);
    }

    [Fact]
    public async Task DeleteProperty_WhenExists_DeletesAndShowsMessage()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeletePropertyAsync(thingId, "testProp")).ReturnsAsync(true);

        await ExecuteHandler($"property {thingId} testProp");

        _brokerMock.Verify(b => b.DeletePropertyAsync(thingId, "testProp"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted property 'testProp' from thing", output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task DeleteProperty_WhenNotFound_ShowsNotFoundMessage()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeletePropertyAsync(thingId, "testProp")).ReturnsAsync(false);

        await ExecuteHandler($"property {thingId} testProp");

        Assert.Contains("Property 'testProp' not found", _writer.ToString());
    }

    // --showguids flag tests

    [Fact]
    public async Task DeleteThing_WithShowGuidsFlag_ShowsGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(true);

        await ExecuteHandler("thing MyThing --showguids");

        var output = _writer.ToString();
        Assert.Contains("Deleted thing", output);
        Assert.Contains("MyThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task DeleteThing_WithoutShowGuidsFlag_HidesGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(true);

        await ExecuteHandler("thing MyThing");

        var output = _writer.ToString();
        Assert.Contains("Deleted thing", output);
        Assert.Contains("MyThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    [Fact]
    public async Task DeleteProperty_WithShowGuidsFlag_ShowsGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeletePropertyAsync(thingId, "testProp")).ReturnsAsync(true);

        await ExecuteHandler("property MyThing testProp --showguids");

        var output = _writer.ToString();
        Assert.Contains("Deleted property 'testProp' from thing", output);
        Assert.Contains("MyThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task DeleteProperty_WithoutShowGuidsFlag_HidesGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);
        _brokerMock.Setup(b => b.DeletePropertyAsync(thingId, "testProp")).ReturnsAsync(true);

        await ExecuteHandler("property MyThing testProp");

        var output = _writer.ToString();
        Assert.Contains("Deleted property 'testProp' from thing", output);
        Assert.Contains("MyThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }
}
