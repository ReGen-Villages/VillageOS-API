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

    private async Task ExecuteHandler(string arg, MyceliumClient? client = null)
    {
        var handler = new RangeCommandHandler(arg, _writer, client);
        await handler.ExecuteAsync();
    }

    #region Help and Basic Commands

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
        await ExecuteHandler("unknown", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Expected range commands:", output);
    }

    #endregion

    #region Create Command Tests

    [Fact]
    public async Task Create_WithMinimumArgs_CallsApi()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp>0\"}");
        // CommandParser splits by space, so criteria is a single token without spaces
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task Create_WithPropertyOption_PassesProperty()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp>0\",\"Property\":\"temp\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", "temp", null))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>0 --property temp", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", "temp", null), Times.Once);
    }

    [Fact]
    public async Task Create_WithBoundsMinOption_CreatesBounds()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", null, It.IsAny<object>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>=20 --bounds-min 20", _myceliumMock.Object);

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
            () => ExecuteHandler($"create {thingId} nominal temp>=20 --bounds-min 20.5 --bounds-max 80.25", _myceliumMock.Object));

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

        await ExecuteHandler($"create {thingId} nominal temp<=80 --bounds-max 80", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp<=80", null, It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithAllOptions_PassesAllParameters()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        // Using criteria without spaces for simplicity in testing
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", "temp", It.IsAny<object>()))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"create {thingId} nominal temp>=20 --property temp --bounds-min 20 --bounds-max 80", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>=20", "temp", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task Create_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("create", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage: range create", output);
    }

    [Fact]
    public async Task Create_TooFewArgs_ShowsUsage()
    {
        var thingId = Guid.NewGuid();
        await ExecuteHandler($"create {thingId} nominal", _myceliumMock.Object);

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

        await ExecuteHandler($"add {thingId} nominal temp>0", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    #endregion

    #region List Command Tests

    [Fact]
    public async Task List_CallsGetRangesAsync()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse($"{{\"ObjectId\":\"{thingId}\",\"OwnRanges\":[]}}");
        _myceliumMock.Setup(b => b.GetRangesAsync(thingId))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"list {thingId}", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.GetRangesAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task List_MissingArg_ShowsUsage()
    {
        await ExecuteHandler("list", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage: range list", output);
    }

    #endregion

    #region Get Command Tests

    [Fact]
    public async Task Get_CallsGetRangeAsync()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\",\"Criteria\":\"temp > 0\"}");
        _myceliumMock.Setup(b => b.GetRangeAsync(thingId, "nominal"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler($"get {thingId} nominal", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.GetRangeAsync(thingId, "nominal"), Times.Once);
    }

    [Fact]
    public async Task Get_RangeNotFound_ShowsError()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetRangeAsync(thingId, "nonexistent"))
            .ReturnsAsync((JsonElement?)null);

        await ExecuteHandler($"get {thingId} nonexistent", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Range not found", output);
    }

    [Fact]
    public async Task Get_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("get", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage: range get", output);
    }

    #endregion

    #region Delete Command Tests

    [Fact]
    public async Task Delete_CallsDeleteRangeAsync()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(true);

        await ExecuteHandler($"delete {thingId} nominal", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.DeleteRangeAsync(thingId, "nominal"), Times.Once);
        var output = _writer.ToString();
        Assert.Contains("Deleted", output);
    }

    [Fact]
    public async Task Delete_Failure_ShowsError()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(false);

        await ExecuteHandler($"delete {thingId} nominal", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Failed to delete", output);
    }

    [Fact]
    public async Task Delete_MissingArgs_ShowsUsage()
    {
        await ExecuteHandler("delete", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage: range delete", output);
    }

    [Fact]
    public async Task Remove_IsAliasForDelete()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.DeleteRangeAsync(thingId, "nominal"))
            .ReturnsAsync(true);

        await ExecuteHandler($"remove {thingId} nominal", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.DeleteRangeAsync(thingId, "nominal"), Times.Once);
    }

    #endregion

    #region Validate Command Tests

    [Fact]
    public async Task Validate_ValidCriteria_ShowsValid()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":true}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("temp > 100"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate temp > 100", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Criteria is valid", output);
    }

    [Fact]
    public async Task Validate_InvalidCriteria_ShowsError()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":false,\"error\":\"Invalid syntax\"}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("invalid"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate invalid", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Invalid criteria", output);
    }

    [Fact]
    public async Task Validate_MissingArg_ShowsUsage()
    {
        await ExecuteHandler("validate", _myceliumMock.Object);

        var output = _writer.ToString();
        Assert.Contains("Usage: range validate", output);
    }

    [Fact]
    public async Task Validate_MultiWordCriteria_JoinsArgs()
    {
        var mockResponse = JsonDocument.Parse("{\"isValid\":true}");
        _myceliumMock.Setup(b => b.ValidateCriteriaAsync("temp > 100 AND rpm < 5000"))
            .ReturnsAsync(mockResponse.RootElement);

        await ExecuteHandler("validate temp > 100 AND rpm < 5000", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.ValidateCriteriaAsync("temp > 100 AND rpm < 5000"), Times.Once);
    }

    #endregion

    #region ParseCreateOption Tests (covers all branches)

    [Fact]
    public async Task ParseCreateOption_UnknownOption_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        // Unknown option should be ignored
        await ExecuteHandler($"create {thingId} nominal temp>0 --unknown value", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_OptionAtEndWithoutValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        // Option without value at end should be ignored (index + 1 >= args.Length)
        await ExecuteHandler($"create {thingId} nominal temp>0 --property", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_InvalidBoundsMinValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        // Non-numeric bounds-min should be ignored
        await ExecuteHandler($"create {thingId} nominal temp>0 --bounds-min not-a-number", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    [Fact]
    public async Task ParseCreateOption_InvalidBoundsMaxValue_IsIgnored()
    {
        var thingId = Guid.NewGuid();
        var mockResponse = JsonDocument.Parse("{\"Name\":\"nominal\"}");
        _myceliumMock.Setup(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null))
            .ReturnsAsync(mockResponse.RootElement);

        // Non-numeric bounds-max should be ignored
        await ExecuteHandler($"create {thingId} nominal temp>0 --bounds-max abc", _myceliumMock.Object);

        _myceliumMock.Verify(b => b.CreateRangeAsync(thingId, "nominal", "temp>0", null, null), Times.Once);
    }

    #endregion
}
