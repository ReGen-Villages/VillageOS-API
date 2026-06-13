using System.Text.Json;
using vos.Taproot;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class NameResolverTests
{
    private readonly Mock<BrokerClient> _brokerMock;

    public NameResolverTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
    }

    [Fact]
    public async Task ResolveThingAsync_WithValidGuid_ReturnsGuidDirectly()
    {
        var expectedId = Guid.NewGuid();
        var resolver = new NameResolver(_brokerMock.Object);

        var result = await resolver.ResolveThingAsync(expectedId.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Id);
        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Never);
    }

    [Fact]
    public async Task ResolveThingAsync_WithUniqueName_ReturnsMatchingId()
    {
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{expectedId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("MyThing");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Id);
    }

    [Fact]
    public async Task ResolveThingAsync_WithUniqueName_CaseInsensitive()
    {
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{expectedId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("MYTHING");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Id);
    }

    [Fact]
    public async Task ResolveThingAsync_WithAmbiguousName_ReturnsError()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{id1}\",\"Name\":\"Duplicate\"}},{{\"Id\":\"{id2}\",\"Name\":\"Duplicate\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("Duplicate");

        Assert.False(result.IsSuccess);
        Assert.Contains("Ambiguous", result.ErrorMessage);
        Assert.Contains(id1.ToString(), result.ErrorMessage);
        Assert.Contains(id2.ToString(), result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveThingAsync_WithNonExistentName_ReturnsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[{\"Id\":\"" + Guid.NewGuid() + "\",\"Name\":\"SomethingElse\"}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("NonExistent");

        Assert.False(result.IsSuccess);
        Assert.Contains("No thing found", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveThingAsync_WithEmptyThingsList_ReturnsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("AnyName");

        Assert.False(result.IsSuccess);
        Assert.Contains("No thing found", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveThingsAsync_CachesTingsForMultipleCalls()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{id1}\",\"Name\":\"Thing1\"}},{{\"Id\":\"{id2}\",\"Name\":\"Thing2\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var results = await resolver.ResolveThingsAsync("Thing1", "Thing2");

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);
        Assert.Equal(id1, results[0].Id);
        Assert.Equal(id2, results[1].Id);

        // Should only call GetAllThingsAsync once due to caching
        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Once);
    }

    [Fact]
    public void ClearCache_ClearsThingsCache()
    {
        var resolver = new NameResolver(_brokerMock.Object);
        resolver.ClearCache(); // Should not throw
    }

    [Fact]
    public async Task ResolveThingAsync_HandlesLowercasePropertyNames()
    {
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"id\":\"{expectedId}\",\"name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveThingAsync("MyThing");

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, result.Id);
    }

    #region ResolveByNameAsync Edge Cases

    [Fact]
    public async Task ResolveByNameAsync_WithNonArrayResponse_ReturnsError()
    {
        // Arrange - Return an object instead of array
        var notAnArray = JsonSerializer.Deserialize<JsonElement>(@"{""error"":""invalid""}");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(notAnArray);

        var resolver = new NameResolver(_brokerMock.Object);

        // Act
        var result = await resolver.ResolveThingAsync("AnyName");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("No things found", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveByNameAsync_WithNullValueKind_ReturnsError()
    {
        // Arrange - Return null
        JsonElement? nullElement = null;
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(nullElement ?? default);

        var resolver = new NameResolver(_brokerMock.Object);

        // Act
        var result = await resolver.ResolveThingAsync("AnyName");

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveByNameAsync_WithThingMissingId_SkipsInvalidThing()
    {
        // Arrange - Thing with matching name but no Id field
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Name\":\"NoIdThing\"}},{{\"Id\":\"{expectedId}\",\"Name\":\"ValidThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);

        // Act - Looking for NoIdThing should fail
        var result = await resolver.ResolveThingAsync("NoIdThing");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("No thing found", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveByNameAsync_WithInvalidGuidFormat_SkipsInvalidThing()
    {
        // Arrange - Thing with invalid GUID
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"not-a-guid\",\"Name\":\"InvalidIdThing\"}},{{\"Id\":\"{expectedId}\",\"Name\":\"ValidThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);

        // Act - Looking for InvalidIdThing should fail
        var result = await resolver.ResolveThingAsync("InvalidIdThing");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("No thing found", result.ErrorMessage);
    }

    #endregion

    #region ResolveNameAsync Tests

    [Fact]
    public async Task ResolveNameAsync_WithKnownGuid_ReturnsName()
    {
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{expectedId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveNameAsync(expectedId.ToString());

        Assert.Equal("MyThing", result);
    }

    [Fact]
    public async Task ResolveNameAsync_WithUnknownGuid_ReturnsGuidString()
    {
        var unknownId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveNameAsync(unknownId.ToString());

        Assert.Equal(unknownId.ToString(), result);
    }

    [Fact]
    public async Task ResolveNameAsync_WithGuidObject_ReturnsName()
    {
        var expectedId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{expectedId}\",\"Name\":\"TestThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var result = await resolver.ResolveNameAsync(expectedId);

        Assert.Equal("TestThing", result);
    }

    #endregion

    #region GetGuidToNameMapAsync Tests

    [Fact]
    public async Task GetGuidToNameMapAsync_ReturnsMapping()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{id1}\",\"Name\":\"Thing1\"}},{{\"Id\":\"{id2}\",\"Name\":\"Thing2\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var map = await resolver.GetGuidToNameMapAsync();

        Assert.Equal(2, map.Count);
        Assert.Equal("Thing1", map[id1.ToString().ToLowerInvariant()]);
        Assert.Equal("Thing2", map[id2.ToString().ToLowerInvariant()]);
    }

    [Fact]
    public async Task GetGuidToNameMapAsync_WithEmptyArray_ReturnsEmptyMap()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var map = await resolver.GetGuidToNameMapAsync();

        Assert.Empty(map);
    }

    [Fact]
    public async Task GetGuidToNameMapAsync_WithNonArrayResponse_ReturnsEmptyMap()
    {
        var notAnArray = JsonSerializer.Deserialize<JsonElement>(@"{""error"":""invalid""}");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(notAnArray);

        var resolver = new NameResolver(_brokerMock.Object);
        var map = await resolver.GetGuidToNameMapAsync();

        Assert.Empty(map);
    }

    [Fact]
    public async Task GetGuidToNameMapAsync_SkipsThingsWithMissingIdOrName()
    {
        var validId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{validId}\",\"Name\":\"ValidThing\"}},{{\"Name\":\"NoId\"}},{{\"Id\":\"{Guid.NewGuid()}\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var resolver = new NameResolver(_brokerMock.Object);
        var map = await resolver.GetGuidToNameMapAsync();

        // Only ValidThing should be in the map
        Assert.Single(map);
        Assert.Equal("ValidThing", map[validId.ToString().ToLowerInvariant()]);
    }

    #endregion

    #region ClearCache Tests

    [Fact]
    public async Task ClearCache_ForcesRefetch()
    {
        var id1 = Guid.NewGuid();
        var things1 = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{id1}\",\"Name\":\"Thing1\"}}]");
        var id2 = Guid.NewGuid();
        var things2 = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{id2}\",\"Name\":\"Thing2\"}}]");

        _brokerMock.SetupSequence(b => b.GetAllThingsAsync())
            .ReturnsAsync(things1)
            .ReturnsAsync(things2);

        var resolver = new NameResolver(_brokerMock.Object);

        // First call uses first response
        var result1 = await resolver.ResolveThingAsync("Thing1");
        Assert.True(result1.IsSuccess);

        // Clear cache
        resolver.ClearCache();

        // Second call should use second response
        var result2 = await resolver.ResolveThingAsync("Thing2");
        Assert.True(result2.IsSuccess);

        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Exactly(2));
    }

    #endregion
}
