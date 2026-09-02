using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Integration tests that test CLI command flows using mocked mycelium responses.
// These tests verify that commands are correctly dispatched and produce expected output.
// For true end-to-end tests, a running mycelium is required.
[Collection(nameof(WorkingDirectoryCollection))]
public class IntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _originalDirectory;
    private readonly Mock<MyceliumClient> _myceliumMock;
    private const string MyceliumUrl = "https://localhost:7243";

    public IntegrationTests()
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"VosTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        _myceliumMock = new Mock<MyceliumClient>(MyceliumUrl) { CallBase = false };
    }

    public void Dispose()
    {
        try
        {
            Directory.SetCurrentDirectory(_originalDirectory);
        }
        catch
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private CommandHandler CreateHandler(StringReader reader, StringWriter writer)
    {
        return new CommandHandler(reader, writer, _myceliumMock.Object, MyceliumUrl);
    }

    [Fact]
    public async Task EndToEnd_CreateThingsAndRelationships_WorksViaCommands()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var likesId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        // Setup mycelium responses for the workflow
        _myceliumMock.Setup(b => b.CreateThingAsync("Alice")).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{aliceId}"",""Name"":""Alice""}}"));
        _myceliumMock.Setup(b => b.CreateThingAsync("Bob")).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{bobId}"",""Name"":""Bob""}}"));
        _myceliumMock.Setup(b => b.CreateThingAsync("likes")).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{likesId}"",""Name"":""likes""}}"));
        _myceliumMock.Setup(b => b.CreateRelationshipAsync(aliceId, likesId, bobId)).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{relationshipId}"",""SubjectId"":""{aliceId}"",""PredicateId"":""{likesId}"",""TargetId"":""{bobId}""}}"));

        var writer = new StringWriter();
        var reader = new StringReader("");
        var handler = CreateHandler(reader, writer);

        // Create Alice
        await handler.HandleCommandAsync("create", "thing Alice");
        Assert.Contains("Created Thing", writer.ToString());

        // Create Bob
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("create", "thing Bob");
        Assert.Contains("Created Thing", writer.ToString());

        // Create relationship
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("create", $"relation {aliceId} {likesId} {bobId}");
        Assert.Contains("Created Relationship", writer.ToString());

        _myceliumMock.Verify(b => b.CreateThingAsync("Alice"), Times.Once);
        _myceliumMock.Verify(b => b.CreateThingAsync("Bob"), Times.Once);
        _myceliumMock.Verify(b => b.CreateRelationshipAsync(aliceId, likesId, bobId), Times.Once);
    }

    [Fact]
    public async Task Integration_ListCommands_DisplayResults()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var thingsJson = $@"[
            {{""Id"":""{aliceId}"",""Name"":""Alice"",""Properties"":{{}}}},
            {{""Id"":""{bobId}"",""Name"":""Bob"",""Properties"":{{}}}}
        ]";
        var relationshipsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{aliceId}"",""TargetId"":""{bobId}""}}
        ]";

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(thingsJson));
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(relationshipsJson));

        // Test list things
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("list", "things");
        var output = writer.ToString();
        Assert.Contains("Alice", output);
        Assert.Contains("Bob", output);

        // Test list relations
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("list", "relations");
        output = writer.ToString();
        Assert.Contains("likes", output);
    }

    [Fact]
    public async Task Integration_FindCommands_ReturnResults()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var likesId = Guid.NewGuid();

        var thingsJson = $@"[
            {{""Id"":""{aliceId}"",""Name"":""Alice"",""Properties"":{{}}}},
            {{""Id"":""{bobId}"",""Name"":""Bob"",""Properties"":{{}}}},
            {{""Id"":""{likesId}"",""Name"":""likes"",""Properties"":{{}}}}
        ]";
        var relationshipsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{aliceId}"",""TargetId"":""{bobId}""}}
        ]";

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(thingsJson));
        _myceliumMock.Setup(b => b.GetThingAsync(aliceId)).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{aliceId}"",""Name"":""Alice""}}"));
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(relationshipsJson));

        // Test find thing
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("find", "thing Ali");
        var output = writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("Alice", output);

        // Test find relationships
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("find", $"relationships {aliceId}");
        output = writer.ToString();
        Assert.Contains("Relationships for thing 'Alice'", output);
        Assert.Contains("As Subject (1)", output);
    }

    [Fact]
    public async Task Integration_TemporalCommands_CallMyceliumApi()
    {
        var mockResponse = JsonDocument.Parse("{\"Timestamp\":\"2026-01-15T12:00:00Z\",\"Things\":[],\"Relationships\":[]}");
        _myceliumMock.Setup(b => b.GetModelAtTimeAsync(null))
            .ReturnsAsync(mockResponse.RootElement);

        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);

        await handler.HandleCommandAsync("temporal", "snapshot now");

        var output = writer.ToString();
        Assert.Contains("Timestamp", output);
        _myceliumMock.Verify(b => b.GetModelAtTimeAsync(null), Times.Once);
    }

    [Fact]
    public async Task Integration_QueryCommands_ReturnStatistics()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var thingsJson = $@"[
            {{""Id"":""{aliceId}"",""Name"":""Alice"",""Properties"":{{""Status"":""Active""}}}},
            {{""Id"":""{bobId}"",""Name"":""Bob"",""Properties"":{{""Status"":""Inactive""}}}}
        ]";
        var relationshipsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{aliceId}"",""TargetId"":""{bobId}""}}
        ]";

        var effectiveJson = $@"{{
            ""{aliceId}"":{{""Status"":{{""Value"":""Active"",""IsInherited"":false}}}},
            ""{bobId}"":{{""Status"":{{""Value"":""Inactive"",""IsInherited"":false}}}}
        }}";

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(thingsJson));
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(relationshipsJson));
        _myceliumMock.Setup(b => b.GetAllPropertiesAsync(It.IsAny<string>())).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>(effectiveJson));

        // Test query stats
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("query", "stats");
        var output = writer.ToString();
        Assert.Contains("Model Statistics", output);
        Assert.Contains("Things: 2", output);
        Assert.Contains("Relationships: 1", output);

        // Test query property
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("query", "property Status Active");
        output = writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("Alice", output);
    }

    [Fact]
    public async Task Integration_DeleteOperations_CallMycelium()
    {
        var aliceId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        _myceliumMock.Setup(b => b.DeletePropertyAsync(aliceId, "Age")).ReturnsAsync(true);
        _myceliumMock.Setup(b => b.DeleteRelationshipAsync(relationshipId)).ReturnsAsync(true);
        _myceliumMock.Setup(b => b.DeleteThingAsync(aliceId)).ReturnsAsync(true);

        // Delete property
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("delete", $"property {aliceId} Age");
        Assert.Contains("Deleted property", writer.ToString());

        // Delete relationship
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("delete", $"relationship {relationshipId}");
        Assert.Contains("Deleted relationship", writer.ToString());

        // Delete thing
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("delete", $"thing {aliceId}");
        Assert.Contains("Deleted thing", writer.ToString());

        _myceliumMock.Verify(b => b.DeletePropertyAsync(aliceId, "Age"), Times.Once);
        _myceliumMock.Verify(b => b.DeleteRelationshipAsync(relationshipId), Times.Once);
        _myceliumMock.Verify(b => b.DeleteThingAsync(aliceId), Times.Once);
    }

    [Fact]
    public async Task Integration_FileSystemCommands_WithRealFiles()
    {
        _myceliumMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync("{\"things\": [], \"relationships\": []}");
        _myceliumMock.Setup(b => b.SetModelAsync(It.IsAny<string>())).ReturnsAsync("OK");

        // Test pwd
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("pwd", null);
        Assert.Contains("Current directory:", writer.ToString());

        // Test cd
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("cd", _testDirectory);
        Assert.Contains("Changed directory to:", writer.ToString());
        Assert.Contains(_testDirectory, writer.ToString());

        // Test serialize to file
        var filePath = Path.Combine(_testDirectory, "integration_test.json");
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("serialize", filePath);
        Assert.True(File.Exists(filePath));

        // Test deserialize from file
        writer = new StringWriter();
        handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("deserialize", filePath);
        Assert.Contains("Model loaded from", writer.ToString());
    }

    [Fact]
    public async Task Integration_GetCommands_RetrieveData()
    {
        var aliceId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        _myceliumMock.Setup(b => b.GetThingAsync(aliceId)).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{aliceId}"",""Name"":""Alice"",""Properties"":{{""Age"":30}}}}"));

        // Test get thing
        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("get", $"thing {aliceId}");
        var output = writer.ToString();
        Assert.Contains("Alice", output);

        _myceliumMock.Verify(b => b.GetThingAsync(aliceId), Times.Once);
    }

    [Fact]
    public async Task Integration_SetProperty_CallsMycelium()
    {
        var aliceId = Guid.NewGuid();

        _myceliumMock.Setup(b => b.SetPropertyAsync(aliceId, "Age", "System.Int32", 25)).ReturnsAsync(
            JsonSerializer.Deserialize<JsonElement>($@"{{""Name"":""Age"",""Value"":25}}"));

        var writer = new StringWriter();
        var handler = CreateHandler(new StringReader(""), writer);
        await handler.HandleCommandAsync("set", $"{aliceId} Age 25");

        _myceliumMock.Verify(b => b.SetPropertyAsync(aliceId, "Age", It.IsAny<string>(), It.IsAny<object>()), Times.Once);
    }
}
