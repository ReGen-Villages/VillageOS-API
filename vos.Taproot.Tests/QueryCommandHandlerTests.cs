using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class QueryCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public QueryCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new QueryCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    // ========== Execute Tests ==========

    [Fact]
    public async Task Execute_WithNoArguments_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("query", output);
    }

    [Fact]
    public async Task Execute_WithUnknownCommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    // ========== Query Property Tests ==========

    [Fact]
    public async Task QueryProperty_WithInsufficientArguments_ShowsUsage()
    {
        await ExecuteHandler("property");

        var output = _writer.ToString();
        Assert.Contains("Usage: query property", output);
    }

    [Fact]
    public async Task QueryProperty_WithNoMatches_ShowsNotFoundMessage()
    {
        var emptyArray = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyArray);

        await ExecuteHandler("property Status Active");

        var output = _writer.ToString();
        Assert.Contains("No things found", output);
    }

    [Fact]
    public async Task QueryProperty_WithMatches_ShowsResults()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing"",""Properties"":{{""Status"":""Active""}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property Status Active");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task QueryProperty_MultipleMatches_ShowsAllResults()
    {
        var thing1Id = Guid.NewGuid();
        var thing2Id = Guid.NewGuid();
        var json = $@"[
            {{""Id"":""{thing1Id}"",""Name"":""Alice"",""Properties"":{{""Status"":""Active""}}}},
            {{""Id"":""{thing2Id}"",""Name"":""Bob"",""Properties"":{{""Status"":""Active""}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property Status Active");

        var output = _writer.ToString();
        Assert.Contains("Found 2 thing(s)", output);
        Assert.Contains("Alice", output);
        Assert.Contains("Bob", output);
    }

    // ========== Query Predicate Tests ==========

    [Fact]
    public async Task QueryPredicate_WithNoArguments_ShowsUsage()
    {
        await ExecuteHandler("predicate");

        var output = _writer.ToString();
        Assert.Contains("Usage: query predicate", output);
    }

    [Fact]
    public async Task QueryPredicate_WithNoMatches_ShowsNotFoundMessage()
    {
        var emptyArray = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyArray);

        await ExecuteHandler("predicate likes");

        var output = _writer.ToString();
        Assert.Contains("No relationships found", output);
    }

    [Fact]
    public async Task QueryPredicate_WithMatches_ShowsResults()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{relId}"",""Name"":""likes"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}""}}]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("predicate likes");

        var output = _writer.ToString();
        Assert.Contains("Found 1 relationship(s)", output);
        Assert.Contains("likes", output);
    }

    [Fact]
    public async Task QueryPredicate_CaseInsensitive_FindsMatches()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{relId}"",""Name"":""Likes"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}""}}]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("predicate likes");

        var output = _writer.ToString();
        Assert.Contains("Found 1 relationship(s)", output);
    }

    // ========== Query Stats Tests ==========

    [Fact]
    public async Task QueryStats_ShowsModelStatistics()
    {
        var thingId = Guid.NewGuid();
        var thingsJson = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing"",""Properties"":{{}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(thingsJson);
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Model Statistics:", output);
        Assert.Contains("Things:", output);
        Assert.Contains("Relationships:", output);
    }

    [Fact]
    public async Task QueryStats_ShowsThingCount()
    {
        var json = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Thing1"",""Properties"":{{}}}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Thing2"",""Properties"":{{}}}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Thing3"",""Properties"":{{}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Things: 3", output);
    }

    [Fact]
    public async Task QueryStats_ShowsRelationshipCount()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""owns"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}}
        ]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Relationships: 2", output);
    }

    [Fact]
    public async Task QueryStats_ShowsPredicateCount()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""owns"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}}
        ]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Predicates: 2", output);
    }

    [Fact]
    public async Task QueryStats_ShowsTopPredicates()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""owns"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}}
        ]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Top Predicates:", output);
        Assert.Contains("likes: 2 relationship(s)", output);
    }

    // ========== Query Path Tests ==========

    [Fact]
    public async Task QueryPath_ShowsNotAvailableMessage()
    {
        await ExecuteHandler("path");

        var output = _writer.ToString();
        Assert.Contains("not available in remote mode", output);
    }

    [Fact]
    public async Task QueryPath_WithArguments_ShowsNotAvailableMessage()
    {
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        await ExecuteHandler($"path {fromId} {toId}");

        var output = _writer.ToString();
        Assert.Contains("not available in remote mode", output);
    }

    [Fact]
    public async Task QueryPath_ExplainsReason()
    {
        await ExecuteHandler("path");

        var output = _writer.ToString();
        Assert.Contains("local graph traversal", output);
    }

    // ========== --showguids Flag Tests ==========

    [Fact]
    public async Task QueryProperty_WithShowGuidsFlag_ShowsGuids()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing"",""Properties"":{{""Status"":""Active""}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property Status Active --showguids");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task QueryProperty_WithoutShowGuidsFlag_HidesGuids()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{""Id"":""{thingId}"",""Name"":""TestThing"",""Properties"":{{""Status"":""Active""}}}}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property Status Active");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    [Fact]
    public async Task QueryPredicate_WithShowGuidsFlag_ShowsGuids()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        var relsJson = $@"[{{""Id"":""{relId}"",""Name"":""likes"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}""}}]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("predicate likes --showguids");

        var output = _writer.ToString();
        Assert.Contains("likes", output);
        Assert.Contains(relId.ToString(), output);
        Assert.Contains(subjectId.ToString(), output);
    }

    [Fact]
    public async Task QueryPredicate_WithoutShowGuidsFlag_HidesGuids()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        var relsJson = $@"[{{""Id"":""{relId}"",""Name"":""likes"",""SubjectId"":""{subjectId}"",""TargetId"":""{targetId}""}}]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("predicate likes");

        var output = _writer.ToString();
        Assert.Contains("likes", output);
        Assert.Contains("Subject", output);
        Assert.Contains("Target", output);
        Assert.DoesNotContain(relId.ToString(), output);
    }

    #region FindPropertyMatch Tests

    [Fact]
    public async Task QueryProperty_InheritedProperty_ShowsInheritedSource()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{
            ""Id"":""{thingId}"",
            ""Name"":""Motor"",
            ""Properties"":{{""localProp"":""localValue""}},
            ""InheritedProperties"":{{
                ""Device"":{{
                    ""SourceName"":""Device"",
                    ""Properties"":{{""serialNumber"":""SN-1234""}}
                }}
            }}
        }}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property serialNumber SN-1234");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("Motor", output);
        Assert.Contains("inherited from Device", output);
    }

    [Fact]
    public async Task QueryProperty_OwnPropertyPreferredOverInherited()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{
            ""Id"":""{thingId}"",
            ""Name"":""Motor"",
            ""Properties"":{{""status"":""active""}},
            ""InheritedProperties"":{{
                ""Device"":{{
                    ""SourceName"":""Device"",
                    ""Properties"":{{""status"":""inactive""}}
                }}
            }}
        }}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property status active");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("Motor", output);
        // Own property should be found (not inherited)
        Assert.DoesNotContain("inherited from", output);
    }

    [Fact]
    public async Task QueryProperty_InheritedWithoutSourceName_UsesSectionKey()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{
            ""Id"":""{thingId}"",
            ""Name"":""Motor"",
            ""Properties"":{{""localProp"":""localValue""}},
            ""InheritedProperties"":{{
                ""Device"":{{
                    ""Properties"":{{""serialNumber"":""SN-1234""}}
                }}
            }}
        }}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property serialNumber SN-1234");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
        Assert.Contains("Motor", output);
        // Uses section key "Device" as fallback source name
        Assert.Contains("inherited from Device", output);
    }

    [Fact]
    public async Task QueryProperty_InheritedPropertiesNotObject_SkipsInherited()
    {
        var thingId = Guid.NewGuid();
        var json = $@"[{{
            ""Id"":""{thingId}"",
            ""Name"":""Motor"",
            ""Properties"":{{""status"":""active""}},
            ""InheritedProperties"":null
        }}]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await ExecuteHandler("property status active");

        var output = _writer.ToString();
        Assert.Contains("Found 1 thing(s)", output);
    }

    #endregion

    #region ShowStatsAsync Tests

    [Fact]
    public async Task QueryStats_EmptyModel_ShowsZeros()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Things: 0", output);
        Assert.Contains("Relationships: 0", output);
        Assert.Contains("Properties: 0", output);
        Assert.DoesNotContain("Top Predicates:", output); // No relationships = no top predicates section
    }

    [Fact]
    public async Task QueryStats_ThingsWithProperties_CountsProperties()
    {
        var json = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Sensor"",""Properties"":{{""temp"":100,""status"":""active""}}}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Motor"",""Properties"":{{""rpm"":3000}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Things: 2", output);
        Assert.Contains("Properties: 3", output); // 2 + 1
    }

    [Fact]
    public async Task QueryStats_HandlerThings_CountsHandlers()
    {
        var json = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""Sensor"",""Properties"":{{""temp"":100}}}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""IsHandler"",""Properties"":{{""ExecutablePath"":""/path/to/exec""}}}}
        ]";
        var thingsArray = JsonSerializer.Deserialize<JsonElement>(json);
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Handlers: 1", output);
    }

    [Fact]
    public async Task QueryStats_NonArrayThingsResponse_HandleGracefully()
    {
        var notArray = JsonSerializer.Deserialize<JsonElement>("{}");
        var emptyRels = JsonSerializer.Deserialize<JsonElement>("[]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(notArray);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(emptyRels);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Things: 0", output);
    }

    [Fact]
    public async Task QueryStats_NonArrayRelsResponse_HandleGracefully()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var notArray = JsonSerializer.Deserialize<JsonElement>("{}");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(notArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Relationships: 0", output);
    }

    [Fact]
    public async Task QueryStats_RelationshipsWithNullNames_HandlesGracefully()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":null,""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""likes"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}}
        ]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Relationships: 2", output);
        Assert.Contains("Top Predicates:", output);
    }

    [Fact]
    public async Task QueryStats_MoreThanFivePredicates_ShowsTopFive()
    {
        var emptyThings = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = $@"[
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p1"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p2"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p3"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p4"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p5"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p6"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}},
            {{""Id"":""{Guid.NewGuid()}"",""Name"":""p6"",""SubjectId"":""{Guid.NewGuid()}"",""TargetId"":""{Guid.NewGuid()}""}}
        ]";
        var relsArray = JsonSerializer.Deserialize<JsonElement>(relsJson);

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyThings);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsArray);

        await ExecuteHandler("stats");

        var output = _writer.ToString();
        Assert.Contains("Top Predicates:", output);
        Assert.Contains("Predicates: 6", output);
        // p6 appears twice so should be first
        Assert.Contains("p6: 2 relationship(s)", output);
    }

    #endregion
}
