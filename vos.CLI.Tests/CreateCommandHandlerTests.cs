using System.Text.Json;
using vos.CLI;
using Moq;
using Xunit;

namespace vos.CLI.Tests;

public class CreateCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public CreateCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    [Fact]
    public async Task CreateThing_ValidName_CreatesThing()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}");
        _brokerMock.Setup(b => b.CreateThingAsync("MyThing")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("thing MyThing", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.CreateThingAsync("MyThing"), Times.Once);
        Assert.Contains("Created Thing", _writer.ToString());
        Assert.Contains("MyThing", _writer.ToString());
        Assert.DoesNotContain(thingId.ToString(), _writer.ToString());
    }

    [Fact]
    public async Task CreateThing_NoName_ShowsUsage()
    {
        var handler = new CreateCommandHandler("thing", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_ValidIds_CreatesRelationship()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        var handler = new CreateCommandHandler($"relation {subjectId} {predicateId} {targetId}", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId), Times.Once);
        Assert.Contains("Created Relationship", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_InvalidSubjectName_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new CreateCommandHandler($"relation not-a-thing {Guid.NewGuid()} {Guid.NewGuid()}", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Error", _writer.ToString());
        Assert.Contains("subject", _writer.ToString().ToLower());
    }

    [Fact]
    public async Task CreateRelation_WithNames_CreatesRelationship()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"Predicate\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        var handler = new CreateCommandHandler("relation Subject Predicate Target", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId), Times.Once);
        Assert.Contains("Created Relationship", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_WithAmbiguousName_ShowsError()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{id1}\",\"Name\":\"Duplicate\"}},{{\"Id\":\"{id2}\",\"Name\":\"Duplicate\"}},{{\"Id\":\"{Guid.NewGuid()}\",\"Name\":\"Other\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new CreateCommandHandler($"relation Duplicate Other {Guid.NewGuid()}", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Ambiguous", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_InsufficientArgs_ShowsUsage()
    {
        var handler = new CreateCommandHandler("relation onlyOneArg", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task CreateProperty_ValidArgs_AddsProperty()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue")).ReturnsAsync(json);

        var handler = new CreateCommandHandler($"property {thingId} TestProp System.String TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue"), Times.Once);
        Assert.Contains("Added property", _writer.ToString());
    }

    [Fact]
    public async Task CreateProperty_WithName_AddsProperty()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("property MyThing TestProp System.String TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue"), Times.Once);
        Assert.Contains("Added property", _writer.ToString());
    }

    [Fact]
    public async Task CreateProperty_InvalidArgCount_ShowsUsage()
    {
        var handler = new CreateCommandHandler("property onlyOneArg", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task UnknownSubCommand_ShowsUsage()
    {
        var handler = new CreateCommandHandler("unknownsubcommand", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    // --showguids flag tests

    [Fact]
    public async Task CreateThing_WithShowGuidsFlag_ShowsGuid()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}");
        _brokerMock.Setup(b => b.CreateThingAsync("MyThing")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("thing MyThing --showguids", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Created Thing", output);
        Assert.Contains("MyThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task CreateThing_WithoutShowGuidsFlag_HidesGuid()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}");
        _brokerMock.Setup(b => b.CreateThingAsync("MyThing")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("thing MyThing", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Created Thing", output);
        Assert.Contains("MyThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    [Fact]
    public async Task CreateRelation_WithShowGuidsFlag_ShowsGuids()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"Predicate\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        var handler = new CreateCommandHandler("relation Subject Predicate Target --showguids", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Created Relationship", output);
        Assert.Contains(subjectId.ToString(), output);
        Assert.Contains(targetId.ToString(), output);
        Assert.Contains(relationshipId.ToString(), output);
    }

    [Fact]
    public async Task CreateRelation_WithoutShowGuidsFlag_HidesGuids()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"Predicate\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        var handler = new CreateCommandHandler("relation Subject Predicate Target", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Created Relationship", output);
        Assert.Contains("Subject", output);
        Assert.Contains("Target", output);
        Assert.DoesNotContain(relationshipId.ToString(), output);
    }

    [Fact]
    public async Task CreateProperty_WithShowGuidsFlag_ShowsGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("property MyThing TestProp System.String TestValue --showguids", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Added property", output);
        Assert.Contains("MyThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task CreateProperty_WithoutShowGuidsFlag_HidesGuid()
    {
        var thingId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"MyThing\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>("{}");
        _brokerMock.Setup(b => b.SetPropertyAsync(thingId, "TestProp", "System.String", "TestValue")).ReturnsAsync(json);

        var handler = new CreateCommandHandler("property MyThing TestProp System.String TestValue", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Added property", output);
        Assert.Contains("MyThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    #region CreateRelationAsync Edge Cases

    [Fact]
    public async Task CreateRelation_InvalidPredicateName_ShowsError()
    {
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new CreateCommandHandler($"relation Subject NonExistentPredicate Target", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Error", output);
        Assert.Contains("predicate", output.ToLower());
    }

    [Fact]
    public async Task CreateRelation_InvalidTargetName_ShowsError()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"Predicate\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new CreateCommandHandler($"relation Subject Predicate NonExistentTarget", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Error", output);
        Assert.Contains("target", output.ToLower());
    }

    [Fact]
    public async Task CreateRelation_TwoArgs_ShowsUsage()
    {
        var handler = new CreateCommandHandler("relation arg1 arg2", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_MixedGuidsAndNames_Works()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"Predicate\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        // Use GUID for subject, name for predicate, GUID for target
        var handler = new CreateCommandHandler($"relation {subjectId} Predicate {targetId}", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        _brokerMock.Verify(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId), Times.Once);
        Assert.Contains("Created Relationship", _writer.ToString());
    }

    [Fact]
    public async Task CreateRelation_DisplaysNamesInOutput()
    {
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();

        var things = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Alice\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"likes\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Bob\"}}]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var json = JsonSerializer.Deserialize<JsonElement>($"{{\"Id\":\"{relationshipId}\"}}");
        _brokerMock.Setup(b => b.CreateRelationshipAsync(subjectId, predicateId, targetId)).ReturnsAsync(json);

        var handler = new CreateCommandHandler("relation Alice likes Bob", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Alice", output);
        Assert.Contains("likes", output);
        Assert.Contains("Bob", output);
    }

    #endregion

    #region CreateProperty Edge Cases

    [Fact]
    public async Task CreateProperty_InvalidThingName_ShowsError()
    {
        var things = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(things);

        var handler = new CreateCommandHandler("property NonExistent TestProp System.String Value", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("Error", output);
    }

    [Fact]
    public async Task CreateProperty_ThreeArgs_ShowsUsage()
    {
        var handler = new CreateCommandHandler("property arg1 arg2 arg3", _writer, _brokerMock.Object);
        await handler.ExecuteAsync();

        Assert.Contains("Usage:", _writer.ToString());
    }

    #endregion
}
