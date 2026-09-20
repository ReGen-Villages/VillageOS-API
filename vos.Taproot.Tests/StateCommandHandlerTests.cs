using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class StateCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public StateCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new StateCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoMycelium_ShowsHelp()
    {
        var handler = new StateCommandHandler("get foo", _writer, client: null);
        await handler.ExecuteAsync();

        var output = _writer.ToString();
        Assert.Contains("State query commands:", output);
        Assert.Contains("state get", output);
    }

    [Fact]
    public async Task Execute_NoArgs_ShowsHelp()
    {
        await ExecuteHandler("");

        Assert.Contains("State query commands:", _writer.ToString());
    }

    [Fact]
    public async Task Get_NoThing_ShowsUsage()
    {
        await ExecuteHandler("get");

        Assert.Contains("Usage: state get", _writer.ToString());
    }

    [Fact]
    public async Task Get_ByGuid_ResolvesAndPrintsStates()
    {
        var thingId = Guid.NewGuid();
        // NameResolver delegates to mycelium.GetThingAsync for GUID lookups
        _myceliumMock.Setup(b => b.GetThingAsync(thingId))
            .ReturnsAsync(Parse($"{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}"));
        _myceliumMock.Setup(b => b.GetStatesAsync(thingId))
            .ReturnsAsync(Parse("[\"warm\",\"dry\"]"));

        await ExecuteHandler($"get {thingId}");

        var output = _writer.ToString();
        Assert.Contains("warm", output);
        Assert.Contains("dry", output);
    }

    [Fact]
    public async Task Get_ByName_ResolvesViaGetAllThingsAsync()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}]"));
        _myceliumMock.Setup(b => b.GetStatesAsync(thingId)).ReturnsAsync(Parse("[\"awake\"]"));

        await ExecuteHandler("get alice");

        Assert.Contains("awake", _writer.ToString());
    }

    [Fact]
    public async Task Get_ResolveFails_PrintsErrorAndDoesNotCallMycelium()
    {
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("get nonexistent");

        var output = _writer.ToString();
        Assert.DoesNotContain("Error:", output); // error path goes through resolveResult.ErrorMessage, not exception
        _myceliumMock.Verify(b => b.GetStatesAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Query_NoStateName_ShowsUsage()
    {
        await ExecuteHandler("query");

        Assert.Contains("Usage: state query", _writer.ToString());
    }

    [Fact]
    public async Task Query_DelegatesToMycelium_PrintsResults()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetThingsInStateAsync("warm"))
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\"}}]"));

        await ExecuteHandler("query warm");

        Assert.Contains(thingId.ToString(), _writer.ToString());
    }

    [Fact]
    public async Task Find_AliasForQuery_DelegatesToMycelium()
    {
        _myceliumMock.Setup(b => b.GetThingsInStateAsync("cold"))
            .ReturnsAsync(Parse("[]"));

        await ExecuteHandler("find cold");

        _myceliumMock.Verify(b => b.GetThingsInStateAsync("cold"), Times.Once);
    }

    [Fact]
    public async Task Relationship_ReadsTheRelationshipStatesRoute()
    {
        var relationshipId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetRelationshipStatesAsync(relationshipId))
            .ReturnsAsync(Parse("{\"States\":[\"strained\"]}"));

        await ExecuteHandler($"relationship {relationshipId}");

        Assert.Contains("\"strained\"", _writer.ToString());
        _myceliumMock.Verify(b => b.GetAllThingsAsync(), Times.Never);
    }

    [Fact]
    public async Task Relationship_WithoutAnId_ShowsUsage()
    {
        await ExecuteHandler("relationship");
        await ExecuteHandler("relationship not-an-id");

        Assert.Contains("Usage: state relationship <id>", _writer.ToString());
        _myceliumMock.Verify(b => b.GetRelationshipStatesAsync(It.IsAny<Guid>()), Times.Never);
    }

    private static readonly DateTime Start = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private Guid SetupSensor()
    {
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\",\"Name\":\"Sensor1\"}}]"));
        return thingId;
    }

    [Fact]
    public async Task History_ReadsTheTransitionsInTheWindowAndPrintsEachWithItsCause()
    {
        var thingId = SetupSensor();
        _myceliumMock.Setup(b => b.GetStateTransitionsAsync(thingId, Start, End)).ReturnsAsync(Parse(
            "{\"ThingName\":\"Sensor1\",\"Coverage\":{\"Source\":\"in-memory\",\"From\":\"2026-09-20T07:00:00Z\",\"To\":\"2026-09-20T12:00:00Z\"},"
            + "\"Transitions\":[{\"At\":\"2026-09-20T09:00:00Z\",\"Entered\":[\"hot\"],\"Exited\":[\"warm\"],\"States\":[\"hot\"],\"TriggeringProperty\":\"temperature\",\"OldValue\":20,\"NewValue\":31}]}"));

        await ExecuteHandler("history Sensor1 2026-09-20T08:00:00Z 2026-09-20T12:00:00Z");

        Assert.Equal(
            "Sensor1: state history, in-memory from 2026-09-20T07:00:00Z to 2026-09-20T12:00:00Z\n"
            + "2026-09-20T09:00:00Z  entered [hot]  left [warm]  (temperature: 20 -> 31)\n",
            _writer.ToString());
    }

    [Fact]
    public async Task History_WithNoWindow_AsksForEverythingAndSaysWhenNothingChanged()
    {
        var thingId = SetupSensor();
        _myceliumMock.Setup(b => b.GetStateTransitionsAsync(thingId, null, null))
            .ReturnsAsync(Parse("{\"ThingName\":\"Sensor1\",\"Coverage\":{\"Source\":\"in-memory\",\"From\":\"a\",\"To\":\"b\"},\"Transitions\":[]}"));

        await ExecuteHandler("history Sensor1");

        Assert.Contains("No state changes in the window.", _writer.ToString());
    }

    [Fact]
    public async Task History_WithATimestampItCannotRead_SaysSoAndAsksNothing()
    {
        SetupSensor();

        await ExecuteHandler("history Sensor1 yesterday");

        Assert.Contains("Invalid timestamp: yesterday", _writer.ToString());
        _myceliumMock.Verify(b => b.GetStateTransitionsAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task Occurrences_ReadsEachSpellInTheStateAndNamesOneStillOpen()
    {
        var thingId = SetupSensor();
        _myceliumMock.Setup(b => b.GetStateOccurrencesAsync(thingId, "hot", Start, null)).ReturnsAsync(Parse(
            "{\"ThingName\":\"Sensor1\",\"StateName\":\"hot\",\"Coverage\":{\"Source\":\"in-memory\",\"From\":\"2026-09-20T07:00:00Z\",\"To\":\"2026-09-20T12:00:00Z\"},"
            + "\"Occurrences\":[{\"EnteredAt\":\"2026-09-20T09:00:00Z\",\"ExitedAt\":\"2026-09-20T10:00:00Z\"},{\"EnteredAt\":\"2026-09-20T11:00:00Z\",\"ExitedAt\":null}]}"));

        await ExecuteHandler("occurrences Sensor1 hot 2026-09-20T08:00:00Z");

        Assert.Equal(
            "Sensor1 in 'hot', in-memory from 2026-09-20T07:00:00Z to 2026-09-20T12:00:00Z\n"
            + "2026-09-20T09:00:00Z -> 2026-09-20T10:00:00Z\n"
            + "2026-09-20T11:00:00Z -> still in it\n",
            _writer.ToString());
    }

    [Fact]
    public async Task Occurrences_NeverInTheState_SaysSo()
    {
        var thingId = SetupSensor();
        _myceliumMock.Setup(b => b.GetStateOccurrencesAsync(thingId, "cold", null, null))
            .ReturnsAsync(Parse("{\"ThingName\":\"Sensor1\",\"StateName\":\"cold\",\"Coverage\":{\"Source\":\"in-memory\",\"From\":\"a\",\"To\":\"b\"},\"Occurrences\":[]}"));

        await ExecuteHandler("occurrences Sensor1 cold");

        Assert.Contains("Never in 'cold' in the window.", _writer.ToString());
    }

    [Fact]
    public async Task HistoryAndOccurrences_WithoutTheirArguments_ShowUsage()
    {
        await ExecuteHandler("history");
        await ExecuteHandler("occurrences Sensor1");

        Assert.Contains("Usage: state history <thing> [start] [end]", _writer.ToString());
        Assert.Contains("Usage: state occurrences <thing> <state-name> [start] [end]", _writer.ToString());
    }

    [Fact]
    public async Task History_WhenThePlatformRefuses_WritesItsWords()
    {
        var thingId = SetupSensor();
        _myceliumMock.Setup(b => b.GetStateTransitionsAsync(thingId, null, null))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable: {\"error\":\"Reactive range evaluation is not active for this model.\"}"));

        await ExecuteHandler("history Sensor1");

        Assert.Contains("Error:", _writer.ToString());
        Assert.Contains("not active for this model", _writer.ToString());
    }

    [Fact]
    public async Task Query_PassesEveryNarrowingOptionToTheRoute()
    {
        var site = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(Parse($"[{{\"Id\":\"{site}\",\"Name\":\"Site-1\"}}]"));
        _myceliumMock.Setup(b => b.GetThingsInStateAsync("warm", It.Is<StateListNarrowing>(n =>
                n.Type == "Home" && n.AlsoIn == "lit,open" && n.NotIn == "empty" && n.Within == site
                && n.Limit == 3 && n.Properties == "area" && n.IncludeArchetypes && !n.CountOnly)))
            .ReturnsAsync(Parse("{\"StateName\":\"warm\",\"Things\":[{\"Id\":\"a\"}]}"));

        await ExecuteHandler("query warm --type=Home --also-in=lit,open --not-in=empty --within=Site-1 --limit=3 --properties=area --include-archetypes");

        Assert.Contains("\"Things\"", _writer.ToString());
    }

    [Fact]
    public async Task Query_WithCount_AsksForTheCountAloneAndPrintsTheNumber()
    {
        _myceliumMock.Setup(b => b.GetThingsInStateAsync("warm", It.Is<StateListNarrowing>(n => n.CountOnly && n.Type == "Home")))
            .ReturnsAsync(Parse("{\"StateName\":\"warm\",\"Count\":7}"));

        await ExecuteHandler("query warm --type=Home --count");

        Assert.Equal("7 thing(s) in state 'warm'.\n", _writer.ToString());
    }

    [Fact]
    public async Task Query_WithinANameTheModelDoesNotHold_SaysSoAndAsksNothing()
    {
        _myceliumMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("query warm --within=Nowhere");

        Assert.Contains("Error:", _writer.ToString());
        _myceliumMock.Verify(b => b.GetThingsInStateAsync(It.IsAny<string>(), It.IsAny<StateListNarrowing>()), Times.Never);
    }

    [Fact]
    public async Task UnknownSubcommand_TreatedAsThingForGet()
    {
        // The default branch in ExecuteSubcommandAsync re-routes the unknown subcommand
        // as the first argument to HandleGetAsync (e.g. "state alice" → state get alice).
        var thingId = Guid.NewGuid();
        _myceliumMock.Setup(b => b.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{thingId}\",\"Name\":\"alice\"}}]"));
        _myceliumMock.Setup(b => b.GetStatesAsync(thingId)).ReturnsAsync(Parse("[]"));

        await ExecuteHandler("alice");

        _myceliumMock.Verify(b => b.GetStatesAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task MyceliumThrows_ErrorWritten()
    {
        _myceliumMock.Setup(b => b.GetThingsInStateAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        await ExecuteHandler("query warm");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("offline", output);
    }
}
