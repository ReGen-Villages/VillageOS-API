using System.Text.Json;
using Moq;
using Xunit;

namespace vos.CLI.Tests;

public class FindCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public FindCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new FindCommandHandler(arg, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Find_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("find thing", output);
        Assert.Contains("find relationships", output);
    }

    [Fact]
    public async Task Find_UnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task FindThing_NoPattern_ShowsUsage()
    {
        await ExecuteHandler("thing");

        Assert.Contains("Usage: find thing <pattern>", _writer.ToString());
    }

    [Fact]
    public async Task FindThing_NoMatches_ShowsNotFoundMessage()
    {
        var emptyArray = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyArray);

        await ExecuteHandler("thing NonExistent");

        Assert.Contains("No things found matching 'NonExistent'", _writer.ToString());
    }

    [Fact]
    public async Task FindThing_WithMatches_ListsMatchingThings()
    {
        var thing1Id = Guid.NewGuid();
        var thing2Id = Guid.NewGuid();
        var thing3Id = Guid.NewGuid();
        var json = $@"[
            {{""Id"":""{thing1Id}"",""Name"":""TestThing1""}},
            {{""Id"":""{thing2Id}"",""Name"":""TestThing2""}},
            {{""Id"":""{thing3Id}"",""Name"":""OtherThing""}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing Test");

        var output = _writer.ToString();
        Assert.Contains("Found 2 thing(s) matching 'Test'", output);
        Assert.DoesNotContain(thing1Id.ToString(), output);
        Assert.Contains("TestThing1", output);
        Assert.DoesNotContain(thing2Id.ToString(), output);
        Assert.Contains("TestThing2", output);
        Assert.DoesNotContain("OtherThing", output);
    }

    [Fact]
    public async Task FindThing_CaseInsensitive_FindsMatches()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing""}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing test");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task FindThing_MultiWordPattern_Works()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""My Test Thing""}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing Test Thing");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s) matching 'Test Thing'", output);
    }

    [Fact]
    public async Task FindThings_Alias_Works()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing""}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("things Test");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
    }

    [Fact]
    public async Task FindRelationships_NoThingId_ShowsUsage()
    {
        await ExecuteHandler("relationships");

        Assert.Contains("Usage: find relationships", _writer.ToString());
    }

    [Fact]
    public async Task FindRelationships_InvalidNameOrGuid_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        await ExecuteHandler("relationships invalid-name");

        Assert.Contains("Error", _writer.ToString());
    }

    [Fact]
    public async Task FindRelationships_WithName_FindsRelationships()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var thingJson = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}");
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);

        var emptyRelationships = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRelationships);

        await ExecuteHandler("relationships MyThing");

        Assert.Contains("No relationships found for thing 'MyThing'", _writer.ToString());
    }

    [Fact]
    public async Task FindRelationships_ThingNotFound_ShowsError()
    {
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync((JsonElement?)null);

        await ExecuteHandler($"relationships {thingId}");

        Assert.Contains($"Thing {thingId} not found", _writer.ToString());
    }

    [Fact]
    public async Task FindRelationships_NoRelationships_ShowsMessage()
    {
        var thingId = Guid.NewGuid();
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""TestThing""}}");
        var emptyRelationships = JsonSerializer.Deserialize<JsonElement>("[]");

        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRelationships);

        await ExecuteHandler($"relationships {thingId}");

        Assert.Contains("No relationships found for thing 'TestThing'", _writer.ToString());
        Assert.DoesNotContain(thingId.ToString(), _writer.ToString());
    }

    [Fact]
    public async Task FindRelationships_AsSubject_ListsOutgoingRelationships()
    {
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{subjectId}"",""Name"":""Subject""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{relationshipId}"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}"",""Name"":""Predicate""}}
        ]");

        _brokerMock.Setup(b => b.GetThingAsync(subjectId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {subjectId}");

        var output = _writer.ToString();
        Assert.Contains("Relationships for thing 'Subject'", output);
        Assert.DoesNotContain(subjectId.ToString(), output);
        Assert.Contains("As Subject (1)", output);
    }

    [Fact]
    public async Task FindRelationships_AsTarget_ListsIncomingRelationships()
    {
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{targetId}"",""Name"":""Target""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{relationshipId}"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}"",""Name"":""Predicate""}}
        ]");

        _brokerMock.Setup(b => b.GetThingAsync(targetId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {targetId}");

        var output = _writer.ToString();
        Assert.Contains("Relationships for thing 'Target'", output);
        Assert.DoesNotContain(targetId.ToString(), output);
        Assert.Contains("As Target (1)", output);
    }

    [Fact]
    public async Task FindRelations_Alias_Works()
    {
        var thingId = Guid.NewGuid();
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""TestThing""}}");
        var emptyRelationships = JsonSerializer.Deserialize<JsonElement>("[]");

        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRelationships);

        await ExecuteHandler($"relations {thingId}");

        Assert.Contains("No relationships found", _writer.ToString());
    }

    // --showguids flag tests

    [Fact]
    public async Task FindThing_WithShowGuidsFlag_ShowsGuids()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing""}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing Test --showguids");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task FindThing_WithoutShowGuidsFlag_HidesGuids()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing""}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing Test");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    [Fact]
    public async Task FindRelationships_WithShowGuidsFlag_ShowsGuids()
    {
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $@"[{{""Id"":""{subjectId}"",""Name"":""Subject""}},{{""Id"":""{targetId}"",""Name"":""Target""}}]");
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{subjectId}"",""Name"":""Subject""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{relationshipId}"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}"",""Name"":""Predicate""}}
        ]");

        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _brokerMock.Setup(b => b.GetThingAsync(subjectId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {subjectId} --showguids");

        var output = _writer.ToString();
        Assert.Contains("Subject", output);
        Assert.Contains("Target", output);
        Assert.Contains(subjectId.ToString(), output);
        Assert.Contains(targetId.ToString(), output);
        Assert.Contains(relationshipId.ToString(), output);
    }

    [Fact]
    public async Task FindRelationships_WithoutShowGuidsFlag_HidesGuids()
    {
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $@"[{{""Id"":""{subjectId}"",""Name"":""Subject""}},{{""Id"":""{targetId}"",""Name"":""Target""}}]");
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{subjectId}"",""Name"":""Subject""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{relationshipId}"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}"",""Name"":""Predicate""}}
        ]");

        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _brokerMock.Setup(b => b.GetThingAsync(subjectId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {subjectId}");

        var output = _writer.ToString();
        Assert.Contains("Subject", output);
        Assert.Contains("Target", output);
        Assert.DoesNotContain(relationshipId.ToString(), output);
    }

    #region FindThingsAsync Edge Cases

    [Fact]
    public async Task FindThing_NonArrayResponse_ShowsNotFound()
    {
        var notArray = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(notArray);

        await ExecuteHandler("thing Test");

        var output = _writer.ToString();
        Assert.Contains("No things found", output);
    }

    [Fact]
    public async Task FindThing_EmptyPattern_ShowsUsage()
    {
        await ExecuteHandler("thing");

        Assert.Contains("Usage: find thing <pattern>", _writer.ToString());
    }

    [Fact]
    public async Task FindThing_PartialMatch_FindsThings()
    {
        var json = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""TemperatureSensor""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""PressureSensor""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Motor""}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("thing Sensor");

        var output = _writer.ToString();
        Assert.Contains("Found 2 thing(s)", output);
        Assert.Contains("TemperatureSensor", output);
        Assert.Contains("PressureSensor", output);
        Assert.DoesNotContain("Motor", output);
    }

    #endregion

    #region FindRelationshipsAsync Edge Cases

    [Fact]
    public async Task FindRelationships_BothSubjectAndTarget_ShowsBothSections()
    {
        var thingId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var rel1Id = Guid.NewGuid();
        var rel2Id = Guid.NewGuid();

        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $@"[{{""Id"":""{thingId}"",""Name"":""Center""}},{{""Id"":""{otherId}"",""Name"":""Other""}}]");
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""Center""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{rel1Id}"",""SubjectId"":""{thingId}"",""TargetId"":""{otherId}"",""Name"":""outgoing""}},
            {{""Id"":""{rel2Id}"",""SubjectId"":""{otherId}"",""TargetId"":""{thingId}"",""Name"":""incoming""}}
        ]");

        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {thingId}");

        var output = _writer.ToString();
        Assert.Contains("As Subject (1)", output);
        Assert.Contains("As Target (1)", output);
        Assert.Contains("outgoing", output);
        Assert.Contains("incoming", output);
    }

    [Fact]
    public async Task FindRelationships_NonArrayRelationships_HandlesGracefully()
    {
        var thingId = Guid.NewGuid();
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""Test""}}");
        var notArray = JsonSerializer.Deserialize<JsonElement>("{}");

        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(notArray);

        await ExecuteHandler($"relationships {thingId}");

        var output = _writer.ToString();
        // Should handle gracefully without crashing
        Assert.Contains("No relationships found", output);
    }

    [Fact]
    public async Task FindRelationships_MultipleAsSubject_ListsAll()
    {
        var subjectId = Guid.NewGuid();
        var target1Id = Guid.NewGuid();
        var target2Id = Guid.NewGuid();

        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $@"[{{""Id"":""{subjectId}"",""Name"":""Subject""}},{{""Id"":""{target1Id}"",""Name"":""Target1""}},{{""Id"":""{target2Id}"",""Name"":""Target2""}}]");
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{subjectId}"",""Name"":""Subject""}}");
        var relationshipsJson = JsonSerializer.Deserialize<JsonElement>($@"[
            {{""Id"":""{Guid.NewGuid()}"",""SubjectId"":""{subjectId}"",""TargetId"":""{target1Id}"",""Name"":""likes""}},
            {{""Id"":""{Guid.NewGuid()}"",""SubjectId"":""{subjectId}"",""TargetId"":""{target2Id}"",""Name"":""owns""}}
        ]");

        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _brokerMock.Setup(b => b.GetThingAsync(subjectId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relationshipsJson);

        await ExecuteHandler($"relationships {subjectId}");

        var output = _writer.ToString();
        Assert.Contains("As Subject (2)", output);
        Assert.Contains("likes", output);
        Assert.Contains("owns", output);
        Assert.Contains("Target1", output);
        Assert.Contains("Target2", output);
    }

    [Fact]
    public async Task FindRelationships_ApiThrowsException_ShowsError()
    {
        var thingId = Guid.NewGuid();
        var thingJson = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""Test""}}");

        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(thingJson);
        _brokerMock.Setup(b => b.GetAllRelationshipsAsync()).ThrowsAsync(new HttpRequestException("Connection failed"));

        await ExecuteHandler($"relationships {thingId}");

        var output = _writer.ToString();
        Assert.Contains("Error", output);
    }

    #endregion
}
