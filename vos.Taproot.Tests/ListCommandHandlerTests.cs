using System.Text.Json;
using vos.Taproot;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class ListCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public ListCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new ListCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task ListThings_WithNoThings_ShowsNoThings()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things");

        Assert.Contains("No things found", _writer.ToString());
    }

    [Fact]
    public async Task ListThings_WithThings_ListsThings()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("Things (1)", output);
        Assert.DoesNotContain(thingId.ToString(), output);
        Assert.Contains("TestThing", output);
    }

    [Fact]
    public async Task ListRelations_WithNoRelationships_ShowsNoRelationships()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(json);

        await ExecuteHandler("relations");

        Assert.Contains("No relationships found", _writer.ToString());
    }

    [Fact]
    public async Task ListRelations_WithRelationships_ListsRelationships()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{relId}\",\"Name\":\"likes\",\"SubjectId\":\"{subjectId}\",\"PredicateId\":\"{predicateId}\",\"TargetId\":\"{targetId}\"}}]");
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(json);

        await ExecuteHandler("relations");

        var output = _writer.ToString();
        Assert.Contains("Relationships (1)", output);
        Assert.Contains("likes", output);
    }

    [Fact]
    public async Task ListPredicates_WithNoRelationships_ShowsNoPredicates()
    {
        var thingsJson = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsJson);

        await ExecuteHandler("predicates");

        Assert.Contains("No predicates found", _writer.ToString());
    }

    [Fact]
    public async Task ListPredicates_WithRelationships_ShowsPredicates()
    {
        var thingsJson = JsonSerializer.Deserialize<JsonElement>("[]");
        var relsJson = JsonSerializer.Deserialize<JsonElement>(
            "[{\"Name\":\"likes\"},{\"Name\":\"owns\"},{\"Name\":\"likes\"}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsJson);

        await ExecuteHandler("predicates");

        var output = _writer.ToString();
        Assert.Contains("Predicates (2)", output);
        Assert.Contains("likes", output);
        Assert.Contains("owns", output);
    }

    [Fact]
    public async Task ListHandlers_WithNoHandlers_ShowsNoHandlers()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("handlers");

        Assert.Contains("No handlers found", _writer.ToString());
    }

    [Fact]
    public async Task ListHandlers_WithHandlers_ShowsHandlers()
    {
        var handlerId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\",\"Properties\":{{\"ExecutablePath\":\"/path/to/exec\",\"RunMode\":\"daemon\"}}}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("handlers");

        var output = _writer.ToString();
        Assert.Contains("Handlers (1)", output);
        Assert.Contains("MyHandler", output);
        Assert.Contains("/path/to/exec", output);
    }

    [Fact]
    public async Task ListServices_WithNoServices_ShowsNoServices()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(json);

        await ExecuteHandler("services");

        Assert.Contains("No running microservices found", _writer.ToString());
    }

    [Fact]
    public async Task ListServices_WithServices_ShowsServices()
    {
        var handlerId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"IsHandler\",\"EndpointUrl\":\"https://localhost:5100\",\"HealthStatus\":\"Healthy\",\"IsRunning\":true}}]");
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(json);

        await ExecuteHandler("services");

        var output = _writer.ToString();
        Assert.Contains("Microservices (1)", output);
        Assert.Contains("IsHandler", output);
        Assert.Contains("Running", output);
        Assert.Contains("https://localhost:5100", output);
        Assert.Contains("Healthy", output);
    }

    [Fact]
    public async Task ListServices_WithStoppedService_ShowsStopped()
    {
        var handlerId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"StoppedService\",\"EndpointUrl\":\"https://localhost:5200\",\"HealthStatus\":\"Unknown\",\"IsRunning\":false}}]");
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(json);

        await ExecuteHandler("services");

        var output = _writer.ToString();
        Assert.Contains("Stopped", output);
        Assert.Contains("StoppedService", output);
    }

    [Fact]
    public async Task List_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        Assert.Contains("Usage:", _writer.ToString());
    }

    // --showguids flag tests

    [Fact]
    public async Task ListThings_WithShowGuidsFlag_ShowsGuids()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things --showguids");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task ListThings_WithoutShowGuidsFlag_HidesGuids()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.DoesNotContain(thingId.ToString(), output);
    }

    [Fact]
    public async Task ListThings_WithShortFlag_ShowsGuids()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($"[{{\"Id\":\"{thingId}\",\"Name\":\"TestThing\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things -g");

        var output = _writer.ToString();
        Assert.Contains("TestThing", output);
        Assert.Contains(thingId.ToString(), output);
    }

    [Fact]
    public async Task ListRelations_WithShowGuidsFlag_ShowsGuids()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"IsA\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        var relsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{relId}\",\"Name\":\"likes\",\"SubjectId\":\"{subjectId}\",\"PredicateId\":\"{predicateId}\",\"TargetId\":\"{targetId}\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsJson);

        await ExecuteHandler("relations --showguids");

        var output = _writer.ToString();
        Assert.Contains("likes", output);
        Assert.Contains(relId.ToString(), output);
        Assert.Contains(subjectId.ToString(), output);
        Assert.Contains(targetId.ToString(), output);
    }

    [Fact]
    public async Task ListRelations_WithoutShowGuidsFlag_ShowsNamesOnly()
    {
        var relId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var predicateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{subjectId}\",\"Name\":\"Subject\"}},{{\"Id\":\"{predicateId}\",\"Name\":\"IsA\"}},{{\"Id\":\"{targetId}\",\"Name\":\"Target\"}}]");
        var relsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{relId}\",\"Name\":\"likes\",\"SubjectId\":\"{subjectId}\",\"PredicateId\":\"{predicateId}\",\"TargetId\":\"{targetId}\"}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ReturnsAsync(relsJson);

        await ExecuteHandler("relations");

        var output = _writer.ToString();
        Assert.Contains("likes", output);
        Assert.Contains("Subject", output);
        Assert.Contains("Target", output);
        Assert.DoesNotContain(relId.ToString(), output);
        Assert.DoesNotContain(subjectId.ToString(), output);
    }

    [Fact]
    public async Task ListHandlers_WithShowGuidsFlag_ShowsGuids()
    {
        var handlerId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\",\"Properties\":{{\"ExecutablePath\":\"/path/to/exec\",\"RunMode\":\"daemon\"}}}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("handlers --showguids");

        var output = _writer.ToString();
        Assert.Contains("MyHandler", output);
        Assert.Contains(handlerId.ToString(), output);
    }

    [Fact]
    public async Task ListHandlers_WithoutShowGuidsFlag_HidesGuids()
    {
        var handlerId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"MyHandler\",\"Properties\":{{\"ExecutablePath\":\"/path/to/exec\",\"RunMode\":\"daemon\"}}}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("handlers");

        var output = _writer.ToString();
        Assert.Contains("MyHandler", output);
        Assert.DoesNotContain(handlerId.ToString(), output);
    }

    [Fact]
    public async Task ListServices_WithShowGuidsFlag_ShowsGuids()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"IsHandler\"}}]");
        var servicesJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"IsHandler\",\"EndpointUrl\":\"https://localhost:5100\",\"HealthStatus\":\"Healthy\",\"IsRunning\":true}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(servicesJson);

        await ExecuteHandler("services --showguids");

        var output = _writer.ToString();
        Assert.Contains("IsHandler", output);
        Assert.Contains(handlerId.ToString(), output);
    }

    [Fact]
    public async Task ListServices_WithoutShowGuidsFlag_HidesGuids()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"IsHandler\"}}]");
        var servicesJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"IsHandler\",\"EndpointUrl\":\"https://localhost:5100\",\"HealthStatus\":\"Healthy\",\"IsRunning\":true}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(servicesJson);

        await ExecuteHandler("services");

        var output = _writer.ToString();
        Assert.Contains("IsHandler", output);
        Assert.DoesNotContain(handlerId.ToString(), output);
    }

    // Agents = alias for services (each service carries its daemon state inline)

    [Fact]
    public async Task ListAgents_WithNone_ShowsNoAgents()
    {
        var emptyJson = JsonSerializer.Deserialize<JsonElement>("[]");
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(emptyJson);
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(emptyJson);

        await ExecuteHandler("agents");

        Assert.Contains("No agents found", _writer.ToString());
    }

    [Fact]
    public async Task ListAgents_WithServices_ShowsServices()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"IsHandler\"}}]");
        var servicesJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"IsHandler\",\"EndpointUrl\":\"https://localhost:5100\",\"HealthStatus\":\"Healthy\",\"IsRunning\":true}}]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(servicesJson);

        await ExecuteHandler("agents");

        var output = _writer.ToString();
        Assert.Contains("Registered Services (1)", output);
        Assert.Contains("IsHandler", output);
    }

    [Fact]
    public async Task ListAgents_WithShowGuidsFlag_ShowsGuids()
    {
        var handlerId = Guid.NewGuid();
        var thingsJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{handlerId}\",\"Name\":\"IsHandler\"}}]");
        var servicesJson = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"HandlerId\":\"{handlerId}\",\"ServiceName\":\"IsHandler\",\"EndpointUrl\":\"https://localhost:5100\",\"HealthStatus\":\"Healthy\",\"IsRunning\":true}}]");

        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsJson);
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(servicesJson);

        await ExecuteHandler("agents --showguids");

        var output = _writer.ToString();
        Assert.Contains("IsHandler", output);
        Assert.Contains(handlerId.ToString(), output);
    }

    #region Unknown Command and Edge Cases

    [Fact]
    public async Task List_UnknownSubcommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task ListThings_ApiThrowsException_ShowsError()
    {
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ThrowsAsync(new HttpRequestException("Connection failed"));

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("Connection failed", output);
    }

    [Fact]
    public async Task ListRelations_ApiThrowsException_ShowsError()
    {
        _myceliumMock.Setup(b => b.GetAllRelationshipsAsync()).ThrowsAsync(new HttpRequestException("Connection failed"));

        await ExecuteHandler("relations");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
    }

    [Fact]
    public async Task ListThings_WithProperties_ShowsProperties()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{thingId}\",\"Name\":\"Sensor\",\"Properties\":{{\"temp\":100,\"status\":\"active\"}}}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("Sensor", output);
        Assert.Contains("temp", output);
        Assert.Contains("100", output);
    }

    [Fact]
    public async Task ListThings_WithInheritedProperties_ShowsInheritedSource()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>(
            $"[{{\"Id\":\"{thingId}\",\"Name\":\"Motor\",\"Properties\":{{\"temp\":50}},\"InheritedProperties\":{{\"Device\":{{\"SourceName\":\"Device\",\"Properties\":{{\"serialNumber\":\"SN-1234\"}}}}}}}}]");
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(json);

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("Motor", output);
        Assert.Contains("temp", output);
        Assert.Contains("serialNumber", output);
        Assert.Contains("Device", output);
    }

    [Fact]
    public async Task ListServices_NonArrayResponse_ShowsError()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}"); // Not an array
        _myceliumMock.Setup(b => b.GetAllServicesAsync()).ReturnsAsync(json);

        await ExecuteHandler("services");

        var output = _writer.ToString();
        Assert.Contains("No running microservices found", output);
    }

    #endregion

    #region Nested-inheritance traversal

    [Fact]
    public async Task ListThings_NestedInheritedProperties_TraversesAllLevels()
    {
        // A thing with nested InheritedProperties.<sourceId>.Inherited.<deeperId>.Properties
        // exercises PushNestedInheritance + GetSourceName recursion. The assertion pins that
        // both the immediate parent and the nested grandparent surface in the rendered output.
        var json = """
        [{
          "Id": "00000000-0000-0000-0000-000000000001",
          "Name": "child",
          "Properties": {},
          "InheritedProperties": {
            "00000000-0000-0000-0000-000000000002": {
              "SourceName": "parent",
              "Properties": { "p1": { "value": "v1" } },
              "Inherited": {
                "00000000-0000-0000-0000-000000000003": {
                  "SourceName": "grandparent",
                  "Properties": { "g1": { "value": "v2" } }
                }
              }
            }
          }
        }]
        """;
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(JsonDocument.Parse(json).RootElement);

        await ExecuteHandler("things");

        var output = _writer.ToString();
        Assert.Contains("parent", output);
        Assert.Contains("grandparent", output);
    }

    #endregion
}
