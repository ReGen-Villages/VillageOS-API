using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class EnginesCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public EnginesCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg)
    {
        var handler = new EnginesCommandHandler(arg, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly string Summary = """
        {"ModelId":"m1","ModelName":"TestModel",
         "Ranges":{"RegisteredRanges":12,"RangesWithBindings":3,"PropertyDependencyEdges":20,
                   "StateDependencyEdges":4,"BindingDependencyEdges":6,"DependencyEdges":30,
                   "EstimatedBytes":17088},
         "Rollups":{"ThingsOwningRollups":2,"RollupProperties":5,"MemberEdges":40,"EstimatedBytes":5120},
         "EstimatedBytesTotal":22208}
        """;

    [Fact]
    public async Task Execute_NoArgs_PrintsBothEnginesTotalsAndTheDrillInHint()
    {
        _myceliumMock.Setup(m => m.GetEngineMetricsAsync()).ReturnsAsync(Parse(Summary));

        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("TestModel", output);
        Assert.Contains("Range evaluation", output);
        Assert.Contains("Reactive computation", output);
        Assert.Contains("12", output);
        Assert.Contains("30", output);
        Assert.Contains("40", output);
        Assert.Contains("16.7 KB", output);
        Assert.Contains("5.0 KB", output);
        Assert.Contains("21.7 KB", output);
        Assert.Contains("engines ranges", output);
        Assert.Contains("engines rollups", output);
    }

    [Fact]
    public async Task Execute_Ranges_ListsEachRangeReactor()
    {
        _myceliumMock.Setup(m => m.GetEngineReactorsAsync()).ReturnsAsync(Parse("""
            {"Ranges":[{"OwnerId":"a","OwnerName":"Tank-1","RangeName":"low_quantity",
                        "WatchedProperties":["quantity"],"WatchedStates":[],
                        "DependencyEdges":1,"BindingEdges":0,"EstimatedBytes":1184}],
             "Rollups":[]}
            """));

        await ExecuteHandler("ranges");

        var output = _writer.ToString();
        Assert.Contains("Tank-1", output);
        Assert.Contains("low_quantity", output);
        Assert.Contains("quantity", output);
        Assert.Contains("1.2 KB", output);
    }

    [Fact]
    public async Task Execute_Rollups_ListsEachRollupReactor()
    {
        _myceliumMock.Setup(m => m.GetEngineReactorsAsync()).ReturnsAsync(Parse("""
            {"Ranges":[],
             "Rollups":[{"OwnerId":"b","OwnerName":"SolarArray","PropertyName":"total_pv_area",
                         "Function":"Sum","Predicate":"is","Direction":"Incoming",
                         "RelatedType":"SolarArray","PropertyPath":"area",
                         "MemberEdges":3,"EstimatedBytes":704}]}
            """));

        await ExecuteHandler("rollups");

        var output = _writer.ToString();
        Assert.Contains("SolarArray", output);
        Assert.Contains("total_pv_area", output);
        Assert.Contains("Sum", output);
        Assert.Contains("area", output);
        Assert.Contains("704 B", output);
    }

    [Fact]
    public async Task Execute_EmptyDrillIn_SaysSoInsteadOfPrintingNothing()
    {
        _myceliumMock.Setup(m => m.GetEngineReactorsAsync())
            .ReturnsAsync(Parse("""{"Ranges":[],"Rollups":[]}"""));

        await ExecuteHandler("ranges");

        Assert.Contains("No range reactors", _writer.ToString());
    }

    [Fact]
    public async Task Execute_RangeWithNoWatchesAndABigFootprint_PrintsNoneAndTheLargerUnit()
    {
        _myceliumMock.Setup(m => m.GetEngineReactorsAsync()).ReturnsAsync(Parse("""
            {"Ranges":[{"OwnerId":"a","OwnerName":"Tank-2","RangeName":"scheduled_check",
                        "WatchedProperties":[],"WatchedStates":[],
                        "DependencyEdges":0,"BindingEdges":0,"EstimatedBytes":5242880}],
             "Rollups":[]}
            """));

        await ExecuteHandler("ranges");

        var output = _writer.ToString();
        Assert.Contains("(none)", output);
        Assert.Contains("5.0 MB", output);
    }

    [Fact]
    public async Task Execute_RollupWithoutAPropertyPath_PrintsTheBareReduction()
    {
        _myceliumMock.Setup(m => m.GetEngineReactorsAsync()).ReturnsAsync(Parse("""
            {"Ranges":[],
             "Rollups":[{"OwnerId":"b","OwnerName":"SolarArray","PropertyName":"panel_count",
                         "Function":"Count","Predicate":"is","Direction":"Incoming",
                         "RelatedType":"SolarArray","PropertyPath":null,
                         "MemberEdges":7,"EstimatedBytes":960}]}
            """));

        await ExecuteHandler("rollups");

        var output = _writer.ToString();
        Assert.Contains("panel_count = Count over Incoming is", output);
        Assert.DoesNotContain("Count(", output);
    }

    [Fact]
    public async Task Execute_WhenTheClientFails_PrintsTheErrorInsteadOfThrowing()
    {
        _myceliumMock.Setup(m => m.GetEngineMetricsAsync())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await ExecuteHandler("");

        Assert.Contains("Error: connection refused", _writer.ToString());
    }

    [Fact]
    public async Task Execute_UnknownSubcommand_ShowsUsage()
    {
        await ExecuteHandler("bogus");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("engines ranges", output);
        Assert.Contains("engines rollups", output);
    }
}
