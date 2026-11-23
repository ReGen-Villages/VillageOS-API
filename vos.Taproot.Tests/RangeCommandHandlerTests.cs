using System.Text.Json;
using FluentAssertions;
using Moq;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

public class RangeCommandHandlerTests
{
    private readonly StringWriter _writer;
    private readonly Mock<MyceliumClient> _myceliumMock;

    public RangeCommandHandlerTests()
    {
        _writer = new StringWriter();
        _myceliumMock = new Mock<MyceliumClient>("http://localhost:5000");
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new RangeCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Range_WithoutClient_ShowsHelp()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Expected range commands:", output);
    }

    [Fact]
    public async Task Range_UnknownSubcommand_ShowsHelp()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Expected range commands:", output);
    }

    [Fact]
    public async Task Create_WithMinimumArgs_CallsApi()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp>0\"}");
        // CommandParser splits by space, so criteria is a single token without spaces
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task Create_WithPropertyOption_PassesProperty()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp>0\",\"Property\":\"temp\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", "temp", null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --property temp");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", "temp", null), Times.Once);
    }

    [Fact]
    public async Task Create_WithBoundsMinOption_CreatesBounds()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", null, It.IsAny<object>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>=20 --bounds-min 20");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", null, It.IsAny<object>()), Times.Once);
    }

    // A bound typed on the command line becomes stored model data, so the same command must mean the
    // same number on every machine. A regional format that writes 20,5 must not turn --bounds-min 20.5
    // into 205.
    [Fact]
    public async Task Create_BoundsWithADecimalPoint_MeansTheSameWhateverTheRegionalFormat()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        object? capturedBounds = null;
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", null, It.IsAny<object>()))
            .Callback<Guid, string, string, string?, object?>((_, _, _, _, bounds) => capturedBounds = bounds)
            .ReturnsAsync(mockResponse.RootElement);

        await TestCulture.InAsync(TestCulture.CommaDecimal,
            () => ExecuteHandler($"create {thingId} nominal temp>=20 --bounds-min 20.5 --bounds-max 80.25"));

        capturedBounds.Should().NotBeNull();
        var boundsType = capturedBounds!.GetType();
        boundsType.GetProperty("Min")!.GetValue(capturedBounds).Should().Be(20.5m);
        boundsType.GetProperty("Max")!.GetValue(capturedBounds).Should().Be(80.25m);
    }

    [Fact]
    public async Task Create_WithBoundsMaxOption_CreatesBounds()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp<=80", null, It.IsAny<object>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp<=80 --bounds-max 80");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp<=80", null, It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithAllOptions_PassesAllParameters()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", "temp", It.IsAny<object>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>=20 --property temp --bounds-min 20 --bounds-max 80");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", "temp", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("create");

        var output = _writer.ToString();
        Assert.Contains("Usage: range create", output);
    }

    [Fact]
    public async Task Create_TooFewArgs_ShowsUsage()
    {
        var thingId = Guid.NewGuid();
        await ExecuteHandler($"create {thingId} nominal");

        var output = _writer.ToString();
        Assert.Contains("Usage: range create", output);
    }

    [Fact]
    public async Task Add_IsAliasForCreate()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"add {thingId} nominal temp>0");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task List_CallsGetRangesAsync()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ObjectId\":\"{thingId}\",\"OwnRanges\":[]}}");
        _myceliumMock.Setup(b => b.GetRangesAsync(thingId))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"list {thingId}");

        _myceliumMock.Verify(b => b.GetRangesAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task List_MissingArg_ShowsUsage()
    {
        await ExecuteHandler("list");

        var output = _writer.ToString();
        Assert.Contains("Usage: range list", output);
    }

    [Fact]
    public async Task List_Relationship_ReadsTheRelationshipRangesRoute()
    {
        var relationshipId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetRelationshipRangesAsync(relationshipId))
            .ReturnsAsync(JsonDocument.Parse("{\"OwnRanges\":[{\"Name\":\"strained\"}]}").RootElement);

        await ExecuteHandler($"list relationship {relationshipId}");

        Assert.Contains("\"strained\"", _writer.ToString());
        _myceliumMock.Verify(b => b.GetAllThingsAsync(), Times.Never);
    }

    [Fact]
    public async Task List_RelationshipWithoutAnId_ShowsUsage()
    {
        await ExecuteHandler("list relationship");
        await ExecuteHandler("list relationship not-an-id");

        Assert.Contains("Usage: range list relationship <id>", _writer.ToString());
        _myceliumMock.Verify(b => b.GetRelationshipRangesAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Get_CallsGetRangeAsync()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp > 0\"}");
        _myceliumMock.Setup(b => b.GetRangeAsync(thingId, "nominal"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"get {thingId} nominal");

        _myceliumMock.Verify(b => b.GetRangeAsync(thingId, "nominal"), Times.Once);
    }

    [Fact]
    public async Task Get_RangeNotFound_ShowsError()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetRangeAsync(thingId, "nonexistent"))
            .ReturnsAsync((JsonElement?)null);

        await ExecuteHandler($"get {thingId} nonexistent");

        var output = _writer.ToString();
        Assert.Contains("Range not found", output);
    }

    [Fact]
    public async Task Get_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("get");

        var output = _writer.ToString();
        Assert.Contains("Usage: range get", output);
    }

    [Fact]
    public async Task Delete_CallsDeleteRangeAsync()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(true);

        await ExecuteHandler($"delete {thingId} nominal");

        _myceliumMock.Verify(b => b.DeleteRangeAsync(thingId, "nominal"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted", output);
    }

    [Fact]
    public async Task Delete_OfARangeTheThingDoesNotHold_SaysNotFound()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(false);

        await ExecuteHandler($"delete {thingId} nominal");

        var output = _writer.ToString();
        Assert.Contains("Range not found: nominal", output);
    }

    [Fact]
    public async Task Delete_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("delete");

        var output = _writer.ToString();
        Assert.Contains("Usage: range delete", output);
    }

    [Fact]
    public async Task Remove_IsAliasForDelete()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(true);

        await ExecuteHandler($"remove {thingId} nominal");

        _myceliumMock.Verify(b => b.DeleteRangeAsync(thingId, "nominal"), Times.Once);
    }

    [Fact]
    public async Task Validate_ValidCriteria_ShowsValid()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":true}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("temp > 100"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate temp > 100");

        var output = _writer.ToString();
        Assert.Contains("Criteria is valid", output);
    }

    [Fact]
    public async Task Validate_InvalidCriteria_ShowsError()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":false,\"error\":\"Invalid syntax\"}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("invalid"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate invalid");

        var output = _writer.ToString();
        Assert.Contains("Invalid criteria", output);
    }

    [Fact]
    public async Task Validate_MissingArg_ShowsUsage()
    {
        await ExecuteHandler("validate");

        var output = _writer.ToString();
        Assert.Contains("Usage: range validate", output);
    }

    [Fact]
    public async Task Validate_MultiWordCriteria_JoinsArgs()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":true}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("temp > 100 AND rpm < 5000"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate temp > 100 AND rpm < 5000");

        _myceliumMock.Verify(b => b.ValidateCriteriaAsync("temp > 100 AND rpm < 5000"), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_UnknownOption_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --unknown value");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_OptionAtEndWithoutValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --property");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_InvalidBoundsMinValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --bounds-min not-a-number");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_InvalidBoundsMaxValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --bounds-max abc");

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }
}
