using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class PipelineCommandHandlerTests
{
    private static readonly Guid PipelineArchetypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RunArchetypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OfPredicateId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid NightlyId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OtherPipelineId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid EarlyRunId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid LateRunId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid OtherRunId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new PipelineCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Thing(Guid id, string name, bool isArchetype = false) =>
        $"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"IsArchetype\":{(isArchetype ? "true" : "false")},\"Properties\":{{}}}}";

    private static string Edge(Guid subject, Guid predicate, Guid target) =>
        $"{{\"Id\":\"{Guid.NewGuid()}\",\"SubjectId\":\"{subject}\",\"PredicateId\":\"{predicate}\",\"TargetId\":\"{target}\"}}";

    private static string Mark(string flag, bool inherited) =>
        $"\"{flag}\":{{\"Value\":true,\"IsInherited\":{(inherited ? "true" : "false")}}}";

    private static string Held(string name, string value) => $"\"{name}\":{{\"Value\":\"{value}\",\"IsInherited\":false}}";

    private void SetupMarkedModel()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse("["
            + string.Join(",",
                Thing(PipelineArchetypeId, "Pipeline", isArchetype: true),
                Thing(RunArchetypeId, "PipelineRun", isArchetype: true),
                Thing(OfPredicateId, "of"),
                Thing(NightlyId, "Nightly"),
                Thing(OtherPipelineId, "Weekly"),
                Thing(EarlyRunId, "run-early"),
                Thing(LateRunId, "run-late"),
                Thing(OtherRunId, "run-other"))
            + "]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("["
            + string.Join(",",
                Edge(EarlyRunId, OfPredicateId, NightlyId),
                Edge(LateRunId, OfPredicateId, NightlyId),
                Edge(OtherRunId, OfPredicateId, OtherPipelineId))
            + "]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{"
            + $"\"{PipelineArchetypeId}\":{{{Mark(PipelineCommandHandler.PipelineFlag, false)}}},"
            + $"\"{RunArchetypeId}\":{{{Mark(PipelineCommandHandler.PipelineRunFlag, false)}}},"
            + $"\"{NightlyId}\":{{{Mark(PipelineCommandHandler.PipelineFlag, true)}}},"
            + $"\"{EarlyRunId}\":{{{Mark(PipelineCommandHandler.PipelineRunFlag, true)},{Held("startedUtc", "2026-09-20T08:00:00Z")},{Held("status", "succeeded")}}},"
            + $"\"{LateRunId}\":{{{Mark(PipelineCommandHandler.PipelineRunFlag, true)},{Held("startedUtc", "2026-09-20T11:00:00Z")},{Held("status", "running")}}},"
            + $"\"{OtherRunId}\":{{{Mark(PipelineCommandHandler.PipelineRunFlag, true)},{Held("startedUtc", "2026-09-20T12:00:00Z")},{Held("status", "failed")}}}"
            + "}"));
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "Pipeline")))
            .ReturnsAsync(Parse($"[{Thing(NightlyId, "Nightly")},{Thing(OtherPipelineId, "Weekly")}]"));
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "PipelineRun")))
            .ReturnsAsync(Parse($"[{Thing(EarlyRunId, "run-early")},{Thing(LateRunId, "run-late")},{Thing(OtherRunId, "run-other")}]"));
    }

    [Fact]
    public async Task Execute_WithoutASubcommand_ShowsUsage()
    {
        await Execute("");
        await Execute("dance");

        _writer.ToString().Should().Contain("Usage:").And.Contain("pipeline run").And.Contain("pipeline history");
    }

    [Fact]
    public async Task List_FindsPipelinesByTheFlagTheirArchetypeCarries()
    {
        SetupMarkedModel();

        await Execute("list");

        _writer.ToString().Should().Be("Pipelines (2):\n  Nightly\n  Weekly\n");
    }

    [Fact]
    public async Task List_WhenTheModelMarksNoPipelineArchetype_SaysSoAndAsksForNoThings()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse($"[{Thing(NightlyId, "Nightly")}]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{}"));

        await Execute("list");

        _writer.ToString().Should().Contain($"marks no archetype with '{PipelineCommandHandler.PipelineFlag}'");
        _myceliumMock.Verify(c => c.GetThingsAsync(It.IsAny<ThingListNarrowing>()), Times.Never);
    }

    [Fact]
    public async Task Run_PostsThePipelineIdAndParametersToTheOrchestratorAndPrintsTheAnswer()
    {
        SetupMarkedModel();
        _myceliumMock.Setup(c => c.PostToEndpointAsync(PipelineCommandHandler.OrchestratorSubdomain,
                $"{{\"pipelineId\":\"{NightlyId}\",\"params\":{{\"site\": \"north\"}},\"async\":true}}"))
            .ReturnsAsync($"{{\"accepted\":true,\"runId\":\"{LateRunId}\"}}");

        await Execute("run Nightly {\"site\": \"north\"}");

        _writer.ToString().Should().Contain($"\"runId\": \"{LateRunId}\"");
    }

    [Fact]
    public async Task Run_WithWait_AsksForTheResultAndSendsEmptyParametersWhenNoneAreGiven()
    {
        SetupMarkedModel();
        _myceliumMock.Setup(c => c.PostToEndpointAsync(PipelineCommandHandler.OrchestratorSubdomain,
                $"{{\"pipelineId\":\"{NightlyId}\",\"params\":{{}},\"async\":false}}"))
            .ReturnsAsync("{\"success\":true,\"nodes\":[]}");

        await Execute("run Nightly --wait");

        _writer.ToString().Should().Contain("\"success\": true");
    }

    [Fact]
    public async Task Run_WithParametersThatAreNotJson_SendsNothing()
    {
        SetupMarkedModel();

        await Execute("run Nightly {not json");

        _writer.ToString().Should().Contain("not JSON");
        _myceliumMock.Verify(c => c.PostToEndpointAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_WritesTheCancellationFlagOntoTheRun()
    {
        _myceliumMock.Setup(c => c.SetPropertyAsync(LateRunId, "cancelRequested", "string", "true")).ReturnsAsync(Parse("{}"));

        await Execute($"cancel {LateRunId}");

        _writer.ToString().Should().Contain($"Cancellation requested for run {LateRunId}");
        _myceliumMock.Verify(c => c.SetPropertyAsync(LateRunId, "cancelRequested", "string", "true"), Times.Once);
    }

    [Fact]
    public async Task Cancel_WithoutARunId_ShowsUsageAndWritesNothing()
    {
        await Execute("cancel");
        await Execute("cancel run-late");

        _writer.ToString().Should().Contain("Usage: pipeline cancel <run-id>");
        _myceliumMock.Verify(c => c.SetPropertyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>()), Times.Never);
    }

    [Fact]
    public async Task History_ListsTheRunsOfThePipelineNewestFirstAndNoOthers()
    {
        SetupMarkedModel();

        await Execute("history Nightly");

        var output = _writer.ToString();
        output.Should().StartWith("Runs of Nightly, newest first:");
        output.IndexOf("run-late".Length > 0 ? LateRunId.ToString() : "", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf(EarlyRunId.ToString(), StringComparison.Ordinal));
        output.Should().Contain("2026-09-20T11:00:00Z").And.Contain("running").And.Contain("succeeded");
        output.Should().NotContain(OtherRunId.ToString());
    }

    [Fact]
    public async Task History_OfAPipelineNeverRun_SaysSo()
    {
        SetupMarkedModel();
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "PipelineRun"))).ReturnsAsync(Parse("[]"));

        await Execute("history Weekly");

        _writer.ToString().Should().Contain("No runs of Weekly.");
    }
}
