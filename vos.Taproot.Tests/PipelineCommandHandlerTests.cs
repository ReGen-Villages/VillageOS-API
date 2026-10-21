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
    private static readonly Guid LateRunRequestId = Guid.Parse("01999a2b-0000-7000-8000-000000000001");

    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new PipelineCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Thing(Guid id, string name, bool isArchetype = false) =>
        $"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"IsArchetype\":{(isArchetype ? "true" : "false")},\"Properties\":{{}}}}";

    private static string Edge(Guid subject, Guid predicate, Guid target) =>
        $"{{\"Id\":\"{Guid.NewGuid()}\",\"SubjectId\":\"{subject}\",\"PredicateId\":\"{predicate}\",\"TargetId\":\"{target}\"}}";

    private static string Held(string name, string value) => $"\"{name}\":{{\"Value\":\"{value}\",\"IsInherited\":false}}";

    // The archetypes are named nothing like the roles, so only the bindings can say which is which.
    private void SetupBoundModel()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse("["
            + string.Join(",",
                Thing(BindsPredicateId, "binds"),
                Thing(PipelineCommandHandler.PipelineRoleId, "pipeline role"),
                Thing(PipelineCommandHandler.PipelineRunRoleId, "pipelineRun role"),
                Thing(PipelineArchetypeId, "Workflow", isArchetype: true),
                Thing(RunArchetypeId, "WorkflowRun", isArchetype: true),
                Thing(OfPredicateId, "of"),
                Thing(NightlyId, "Nightly"),
                Thing(OtherPipelineId, "Weekly"),
                Thing(EarlyRunId, "run-early"),
                Thing(LateRunId, "run-late"),
                Thing(OtherRunId, "run-other"))
            + "]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("["
            + string.Join(",",
                Edge(PipelineCommandHandler.PipelineRoleId, BindsPredicateId, PipelineArchetypeId),
                Edge(PipelineCommandHandler.PipelineRunRoleId, BindsPredicateId, RunArchetypeId),
                Edge(EarlyRunId, OfPredicateId, NightlyId),
                Edge(LateRunId, OfPredicateId, NightlyId),
                Edge(OtherRunId, OfPredicateId, OtherPipelineId))
            + "]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{"
            + $"\"{EarlyRunId}\":{{{Held("startedUtc", "2026-09-20T08:00:00Z")},{Held("status", "succeeded")}}},"
            + $"\"{LateRunId}\":{{{Held("startedUtc", "2026-09-20T11:00:00Z")},{Held("status", "running")},{Held("requestId", LateRunRequestId.ToString())}}},"
            + $"\"{OtherRunId}\":{{{Held("startedUtc", "2026-09-20T12:00:00Z")},{Held("status", "failed")}}}"
            + "}"));
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "Workflow")))
            .ReturnsAsync(Parse($"[{Thing(NightlyId, "Nightly")},{Thing(OtherPipelineId, "Weekly")}]"));
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "WorkflowRun")))
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
    public async Task List_FindsPipelinesThroughTheRoleBindingTheirArchetype()
    {
        SetupBoundModel();

        await Execute("list");

        _writer.ToString().Should().Be("Pipelines (2):\n  Nightly\n  Weekly\n");
    }

    [Fact]
    public async Task List_WhenTheModelBindsNoPipelineArchetype_SaysSoAndAsksForNoThings()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse($"[{Thing(NightlyId, "Nightly")}]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{}"));

        await Execute("list");

        _writer.ToString().Should().Contain("binds no archetype to the pipeline role");
        _myceliumMock.Verify(c => c.GetThingsAsync(It.IsAny<ThingListNarrowing>()), Times.Never);
    }

    [Fact]
    public async Task Run_PostsThePipelineIdAndParametersToTheOrchestratorAndPrintsTheAnswer()
    {
        SetupBoundModel();
        _myceliumMock.Setup(c => c.PostToEndpointAsync(PipelineCommandHandler.OrchestratorSubdomain,
                $"{{\"pipelineId\":\"{NightlyId}\",\"parameters\":{{\"site\": \"north\"}},\"async\":true}}"))
            .ReturnsAsync($"{{\"accepted\":true,\"runId\":\"{LateRunId}\"}}");

        await Execute("run Nightly {\"site\": \"north\"}");

        _writer.ToString().Should().Contain($"\"runId\": \"{LateRunId}\"");
    }

    [Fact]
    public async Task Run_WithWait_AsksForTheResultAndSendsEmptyParametersWhenNoneAreGiven()
    {
        SetupBoundModel();
        _myceliumMock.Setup(c => c.PostToEndpointAsync(PipelineCommandHandler.OrchestratorSubdomain,
                $"{{\"pipelineId\":\"{NightlyId}\",\"parameters\":{{}},\"async\":false}}"))
            .ReturnsAsync("{\"success\":true,\"nodes\":[]}");

        await Execute("run Nightly --wait");

        _writer.ToString().Should().Contain("\"success\": true");
    }

    [Fact]
    public async Task Run_WithParametersThatAreNotJson_SendsNothing()
    {
        SetupBoundModel();

        await Execute("run Nightly {not json");

        _writer.ToString().Should().Contain("not JSON");
        _myceliumMock.Verify(c => c.PostToEndpointAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static readonly Guid IsPredicateId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid BindsPredicateId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid HaltKindId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006");
    private static readonly Guid HaltsConnectionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");
    private static readonly Guid StopRequestId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private void SetupAModelThatStopsRuns(bool bindsStopping = true)
    {
        var things = new List<string> { Thing(IsPredicateId, "is"), Thing(BindsPredicateId, "binds"), Thing(LateRunId, "run-late") };
        var edges = new List<string>();
        if (bindsStopping)
        {
            things.AddRange([Thing(HaltKindId, "Halt", isArchetype: true), Thing(HaltsConnectionId, "halts"),
                Thing(PipelineCommandHandler.StopRequestRoleId, "stopRequest role"), Thing(PipelineCommandHandler.RunStoppingRoleId, "runStopping role")]);
            edges.AddRange([Edge(PipelineCommandHandler.StopRequestRoleId, BindsPredicateId, HaltKindId),
                Edge(PipelineCommandHandler.RunStoppingRoleId, BindsPredicateId, HaltsConnectionId)]);
        }
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse("[" + string.Join(",", things) + "]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[" + string.Join(",", edges) + "]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{}"));
        _myceliumMock.Setup(c => c.CreateThingAsync(It.IsAny<string>(), false)).ReturnsAsync(Parse($"{{\"Id\":\"{StopRequestId}\"}}"));
        _myceliumMock.Setup(c => c.CreateRelationshipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(Parse("{}"));
        _myceliumMock.Setup(c => c.SetPropertyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>())).ReturnsAsync(Parse("{}"));
    }

    [Fact]
    public async Task Cancel_WritesAStopRequestWithItsReasonAndRelatesItToTheRunAlongTheBoundConnection()
    {
        SetupAModelThatStopsRuns();

        await Execute($"cancel {LateRunId} the survey was withdrawn");

        _writer.ToString().Should().Contain($"Stop requested for run {LateRunId}");
        _myceliumMock.Verify(c => c.CreateRelationshipAsync(StopRequestId, IsPredicateId, HaltKindId), Times.Once);
        _myceliumMock.Verify(c => c.SetPropertyAsync(StopRequestId, "reason", "string", "the survey was withdrawn"), Times.Once);
        _myceliumMock.Verify(c => c.CreateRelationshipAsync(StopRequestId, HaltsConnectionId, LateRunId), Times.Once);
    }

    [Fact]
    public async Task Cancel_InAModelThatBindsNothingToStoppingRuns_SaysSoAndWritesNothing()
    {
        SetupAModelThatStopsRuns(bindsStopping: false);

        await Execute($"cancel {LateRunId}");

        _writer.ToString().Should().Contain("binds nothing to stopping a run");
        _myceliumMock.Verify(c => c.CreateThingAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_WithoutARunId_ShowsUsageAndWritesNothing()
    {
        await Execute("cancel");
        await Execute("cancel run-late");

        _writer.ToString().Should().Contain("Usage: pipeline cancel <run-id> [reason]");
        _myceliumMock.Verify(c => c.CreateThingAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task History_ListsTheRunsOfThePipelineNewestFirstAndNoOthers()
    {
        SetupBoundModel();

        await Execute("history Nightly");

        var output = _writer.ToString();
        output.Should().StartWith("Runs of Nightly, newest first:");
        output.IndexOf(LateRunId.ToString(), StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf(EarlyRunId.ToString(), StringComparison.Ordinal));
        output.Should().Contain("2026-09-20T11:00:00Z").And.Contain("running").And.Contain("succeeded");
        output.Should().NotContain(OtherRunId.ToString());
    }

    [Fact]
    public async Task History_NamesTheRequestARunStartedFromAndNothingForARunStartedWithoutOne()
    {
        SetupBoundModel();

        await Execute("history Nightly");

        var lines = _writer.ToString().Split('\n');
        lines.Single(line => line.Contains(LateRunId.ToString())).Should().EndWith($"request {LateRunRequestId}");
        lines.Single(line => line.Contains(EarlyRunId.ToString())).Should().NotContain("request");
    }

    [Fact]
    public async Task History_WhenTheModelBindsNoRunArchetype_SaysSoAndAsksForNoThings()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse($"[{Thing(NightlyId, "Nightly")}]"));
        _myceliumMock.Setup(c => c.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[]"));
        _myceliumMock.Setup(c => c.GetAllPropertiesAsync("effective")).ReturnsAsync(Parse("{}"));
        _myceliumMock.Setup(c => c.GetThingAsync(NightlyId)).ReturnsAsync((JsonElement?)Parse(Thing(NightlyId, "Nightly")));

        await Execute($"history {NightlyId}");

        _writer.ToString().Should().Contain("binds no archetype to the pipeline run role");
        _myceliumMock.Verify(c => c.GetThingsAsync(It.IsAny<ThingListNarrowing>()), Times.Never);
    }

    [Fact]
    public async Task History_OfAPipelineNeverRun_SaysSo()
    {
        SetupBoundModel();
        _myceliumMock.Setup(c => c.GetThingsAsync(It.Is<ThingListNarrowing>(n => n.Type == "WorkflowRun"))).ReturnsAsync(Parse("[]"));

        await Execute("history Weekly");

        _writer.ToString().Should().Contain("No runs of Weekly.");
    }
}
