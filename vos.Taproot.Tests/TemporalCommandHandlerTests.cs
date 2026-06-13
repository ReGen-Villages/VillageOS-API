using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class TemporalCommandHandlerTests
{
    private readonly StringWriter _writer;
    private readonly Mock<BrokerClient> _brokerMock;

    public TemporalCommandHandlerTests()
    {
        _writer = new StringWriter();
        _brokerMock = new Mock<BrokerClient>("http://localhost:5000");
    }

    private async Task ExecuteHandler(string arg, BrokerClient? client = null)
    {
        var handler = new TemporalCommandHandler(arg, _writer, client);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Temporal_WithoutClient_ShowsHelp()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Temporal query commands:", output);
        Assert.Contains("temporal snapshot", output);
        Assert.Contains("temporal history", output);
        Assert.Contains("temporal at", output);
    }

    [Fact]
    public async Task Temporal_ListsAvailableCommands()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("temporal snapshot", output);
        Assert.Contains("temporal history", output);
        Assert.Contains("temporal at", output);
    }

    [Fact]
    public async Task Temporal_Snapshot_WithClient_CallsApi()
    {
        var mockResponse = JsonDocument.Parse("{\"Timestamp\":\"2026-01-15T12:00:00Z\",\"Things\":[],\"Relationships\":[]}");
        _brokerMock.Setup(b => b.GetModelAtTimeAsync(null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("snapshot now", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Timestamp", output);
        _brokerMock.Verify(b => b.GetModelAtTimeAsync(null), Times.Once);
    }

    [Fact]
    public async Task Temporal_Snapshot_WithTimestamp_PassesTimestampToApi()
    {
        var mockResponse = JsonDocument.Parse("{\"Timestamp\":\"2026-01-15T12:00:00Z\",\"Things\":[],\"Relationships\":[]}");
        _brokerMock.Setup(b => b.GetModelAtTimeAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("snapshot 2026-01-15T12:00:00Z", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetModelAtTimeAsync(It.Is<DateTime?>(d => d.HasValue)), Times.Once);
    }

    [Fact]
    public async Task Temporal_History_CallsApiWithCorrectParameters()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ThingId\":\"{thingId}\",\"PropertyName\":\"status\",\"Versions\":[]}}");
        _brokerMock.Setup(b => b.GetPropertyVersionsAsync(thingId, "status", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"history {thingId} status", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetPropertyVersionsAsync(thingId, "status", null, null), Times.Once);
    }

    [Fact]
    public async Task Temporal_History_InvalidThingId_ShowsError()
    {
        await ExecuteHandler("history not-a-guid status", _brokerMock.Object);

        var output = _writer.ToString();
        // NameResolver returns "No things found. Cannot resolve name" when no things exist in the model
        Assert.Contains("Cannot resolve name", output);
    }

    [Fact]
    public async Task Temporal_History_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("history", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task Temporal_At_CallsApiWithCorrectParameters()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"Id\":\"{thingId}\",\"Name\":\"Test\",\"Properties\":{{}}}}");
        _brokerMock.Setup(b => b.GetThingAtTimeAsync(thingId, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"at {thingId} now", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetThingAtTimeAsync(thingId, null), Times.Once);
    }

    [Fact]
    public async Task Temporal_At_WithTimestamp_PassesTimestampToApi()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"Id\":\"{thingId}\",\"Name\":\"Test\",\"Properties\":{{}}}}");
        _brokerMock.Setup(b => b.GetThingAtTimeAsync(thingId, It.IsAny<DateTime?>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"at {thingId} 2026-01-15T12:00:00Z", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetThingAtTimeAsync(thingId, It.Is<DateTime?>(d => d.HasValue)), Times.Once);
    }

    [Fact]
    public async Task Temporal_At_ThingNotFound_ShowsError()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetThingAtTimeAsync(thingId, It.IsAny<DateTime?>()))
            .ReturnsAsync((JsonElement?)null);

        await ExecuteHandler($"at {thingId} now", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Thing not found", output);
    }

    [Fact]
    public async Task Temporal_At_ApiError_ShowsErrorMessage()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetThingAtTimeAsync(thingId, It.IsAny<DateTime?>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        await ExecuteHandler($"at {thingId} now", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
    }

    [Fact]
    public async Task Temporal_Snapshot_InvalidTimestamp_ShowsError()
    {
        await ExecuteHandler("snapshot not-a-timestamp", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Invalid timestamp", output);
    }

    [Fact]
    public async Task Temporal_UnknownSubcommand_ShowsHelp()
    {
        await ExecuteHandler("unknown", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Temporal query commands:", output);
    }

    #region HandleMutationsAsync Tests

    [Fact]
    public async Task Mutations_WithNoArgs_CallsGetModelMutationsAsync()
    {
        var mockResponse = JsonDocument.Parse("{\"TotalMutations\":0,\"ThingMutations\":{}}");
        _brokerMock.Setup(b => b.GetModelMutationsAsync(null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("mutations", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetModelMutationsAsync(null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_WithModelArg_CallsGetModelMutationsAsync()
    {
        var mockResponse = JsonDocument.Parse("{\"TotalMutations\":0,\"ThingMutations\":{}}");
        _brokerMock.Setup(b => b.GetModelMutationsAsync(null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("mutations model", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetModelMutationsAsync(null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_WithModelAndTimeRange_PassesTimeRange()
    {
        var mockResponse = JsonDocument.Parse("{\"TotalMutations\":0,\"ThingMutations\":{}}");
        _brokerMock.Setup(b => b.GetModelMutationsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("mutations model 2026-01-01T00:00:00Z 2026-01-31T23:59:59Z", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetModelMutationsAsync(
            It.Is<DateTime?>(d => d.HasValue),
            It.Is<DateTime?>(d => d.HasValue)), Times.Once);
    }

    [Fact]
    public async Task Mutations_WithThingArg_CallsGetThingMutationsAsync()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ThingId\":\"{thingId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetThingMutationsAsync(thingId, null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations thing {thingId}", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetThingMutationsAsync(thingId, null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_ThingWithTimeRange_PassesTimeRange()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ThingId\":\"{thingId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetThingMutationsAsync(thingId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations thing {thingId} 2026-01-01T00:00:00Z", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetThingMutationsAsync(thingId, It.Is<DateTime?>(d => d.HasValue), null), Times.Once);
    }

    [Fact]
    public async Task Mutations_ThingMissingName_ShowsUsage()
    {
        await ExecuteHandler("mutations thing", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task Mutations_RelationshipArg_CallsGetRelationshipMutationsAsync()
    {
        var relId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"RelationshipId\":\"{relId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetRelationshipMutationsAsync(relId, null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations relationship {relId}", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetRelationshipMutationsAsync(relId, null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_RelShorthand_CallsGetRelationshipMutationsAsync()
    {
        var relId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"RelationshipId\":\"{relId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetRelationshipMutationsAsync(relId, null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations rel {relId}", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetRelationshipMutationsAsync(relId, null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_RelWithTimeRange_PassesTimeRange()
    {
        var relId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"RelationshipId\":\"{relId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetRelationshipMutationsAsync(relId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations rel {relId} 2026-01-01T00:00:00Z 2026-01-31T23:59:59Z", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetRelationshipMutationsAsync(
            relId,
            It.Is<DateTime?>(d => d.HasValue),
            It.Is<DateTime?>(d => d.HasValue)), Times.Once);
    }

    [Fact]
    public async Task Mutations_RelMissingId_ShowsUsage()
    {
        await ExecuteHandler("mutations rel", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task Mutations_RelInvalidGuid_ShowsError()
    {
        await ExecuteHandler("mutations rel not-a-guid", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Invalid relationship ID", output);
    }

    [Fact]
    public async Task Mutations_ImplicitThingByGuid_CallsGetThingMutationsAsync()
    {
        // When mutation type is not recognized but is a valid GUID, try as thing
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ThingId\":\"{thingId}\",\"Mutations\":[]}}");
        _brokerMock.Setup(b => b.GetThingMutationsAsync(thingId, null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"mutations {thingId}", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetThingMutationsAsync(thingId, null, null), Times.Once);
    }

    [Fact]
    public async Task Mutations_ImplicitThingNotFound_ShowsError()
    {
        await ExecuteHandler("mutations SomeUnknownThing", _brokerMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Cannot resolve name", output);
    }

    #endregion

    #region Snapshot no-timestamp path

    [Fact]
    public async Task Snapshot_NoTimestamp_PassesNullToBroker()
    {
        // Pins the contract that `snapshot` without a trailing timestamp arg routes
        // through ParseOptionalTimestamp's null branch and calls GetModelAtTimeAsync(null).
        _brokerMock.Setup(b => b.GetModelAtTimeAsync(null))
            .ReturnsAsync(JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler("snapshot", _brokerMock.Object);

        _brokerMock.Verify(b => b.GetModelAtTimeAsync(null), Times.AtLeastOnce);
    }

    #endregion
}
