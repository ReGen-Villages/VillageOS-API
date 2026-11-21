using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;
using vos.Service.Shared;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The orchestrator's unit tests hand it a graph they built, so only the real engine shows that the drawing read
// brings each wire's two connector relationships and the ports they reach, and each node's choices with the Things
// they compare with.
public class PhloemReadsAWiresPortsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsAWiresPortsTests(TheEngine engine) => _engine = engine;

    private sealed record TextStudio(Guid Pipeline, Guid Uppercase, Guid Reverse, Guid TextOut, Guid TextIn, Guid? Shout, Guid? Loud,
        Guid? Tell = null, Guid? Office = null, Guid? TellsTheOffice = null);

    private async Task<TextStudio> DrawTextStudioAsync(bool uppercaseChooses, bool tellsTheOffice = false, bool portsOnAPrototype = false,
        string? transformCallParams = null)
    {
        var firstDrawing = await _engine.FindAsync("carries") is null;
        var @is = await _engine.DeclareOnceAsync(PlatformPredicates.Is);
        var has = await _engine.DeclareOnceAsync(ModelNames.Has);
        await _engine.DeclareOnceAsync(ModelNames.Binds);
        var pipelineArchetype = await _engine.PlayingAsync(PipelineRoles.Pipeline, "Pipeline", isArchetype: true);
        var nodeArchetype = await _engine.PlayingAsync(PipelineRoles.PipelineNode, "PipelineNode", isArchetype: true);
        await _engine.PlayingAsync(PipelineRoles.PipelineInput, "PipelineInput", isArchetype: true);
        var outputArchetype = await _engine.PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput", isArchetype: true);
        var connectionArchetype = await _engine.PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection", isArchetype: true);
        var serviceArchetype = await _engine.PlayingAsync(PipelineRoles.Service, "Service", isArchetype: true);
        var portArchetype = await _engine.PlayingAsync(PipelineRoles.Port, "Port", isArchetype: true);
        var wireArchetype = await _engine.PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire", isArchetype: true);
        var inputConnector = await _engine.PlayingAsync(PipelineRoles.InputConnector, "inputConnector", isArchetype: false);
        var outputConnector = await _engine.PlayingAsync(PipelineRoles.OutputConnector, "outputConnector", isArchetype: false);
        var takenOn = await _engine.PlayingAsync(PipelineRoles.TakenOn, "takenOn", isArchetype: false);
        var success = await _engine.PlayingAsync(PipelineRoles.Success, "success", isArchetype: false);
        await _engine.PlayingAsync(PipelineRoles.Fail, "fail", isArchetype: false);
        var decisionPath = await _engine.PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath", isArchetype: true);
        var comparesWith = await _engine.PlayingAsync(PipelineRoles.ComparesWith, "comparesWith", isArchetype: false);
        var carries = await _engine.DeclareOnceAsync("carries");

        var transform = await _engine.DeclareAsync("Transform service", properties: transformCallParams is null
            ? null
            : new Dictionary<string, object?> { [ModelNames.CallParams] = transformCallParams });
        var portHolder = transform;
        if (portsOnAPrototype)
        {
            portHolder = await _engine.DeclareAsync("Transform prototype", isArchetype: true);
            await _engine.RelateAsync(portHolder, @is, serviceArchetype);
        }
        await _engine.RelateAsync(transform, @is, portsOnAPrototype ? portHolder : serviceArchetype);
        async Task<Guid> PortAsync(string direction)
        {
            var port = await _engine.DeclareAsync($"transform.{direction}.text",
                properties: new Dictionary<string, object?> { ["portName"] = "text", ["direction"] = direction, ["type"] = "string" });
            await _engine.RelateAsync(port, @is, portArchetype);
            await _engine.RelateAsync(portHolder, has, port);
            return port;
        }
        var textIn = await PortAsync("in");
        var textOut = await PortAsync("out");
        var errorPort = await _engine.PlayingAsync(PipelineRoles.ErrorPort, "error", isArchetype: false,
            new Dictionary<string, object?> { ["portName"] = "error", ["direction"] = "out", ["type"] = "string" });
        if (firstDrawing) await _engine.RelateAsync(errorPort, @is, portArchetype);

        async Task<Guid> NodeAsync(string name)
        {
            var connection = await _engine.DeclareAsync($"{name} connection",
                properties: new Dictionary<string, object?> { ["Subdomain"] = name.ToLowerInvariant() });
            await _engine.RelateAsync(connection, @is, connectionArchetype);
            await _engine.RelateAsync(connection, has, transform);
            var node = await _engine.DeclareAsync(name);
            await _engine.RelateAsync(node, @is, nodeArchetype);
            await _engine.RelateAsync(node, has, connection);
            return node;
        }
        var uppercase = await NodeAsync("Uppercase");
        var reverse = await NodeAsync("Reverse");
        var pipeline = await _engine.DeclareAsync("Text Studio");
        await _engine.RelateAsync(pipeline, @is, pipelineArchetype);
        await _engine.RelateAsync(pipeline, has, uppercase);
        await _engine.RelateAsync(pipeline, has, reverse);

        if (firstDrawing) await _engine.RelateAsync(outputArchetype, @is, nodeArchetype);
        var end = await _engine.DeclareAsync("Text Studio end");
        await _engine.RelateAsync(end, @is, outputArchetype);
        var reached = await _engine.DeclareAsync("end.text",
            properties: new Dictionary<string, object?> { ["portName"] = "text", ["direction"] = "in", ["type"] = "string" });
        await _engine.RelateAsync(reached, @is, portArchetype);
        await _engine.RelateAsync(end, has, reached);
        await _engine.RelateAsync(pipeline, has, end);

        Guid? shout = null, loud = null;
        if (uppercaseChooses)
        {
            loud = await _engine.DeclareAsync("loud");
            shout = await _engine.DeclareAsync("shout", properties: new Dictionary<string, object?> { [ModelNames.When] = "outputs.volume in $things.id" });
            await _engine.RelateAsync(shout.Value, @is, decisionPath);
            await _engine.RelateAsync(uppercase, has, shout.Value);
            await _engine.RelateAsync(shout.Value, comparesWith, loud.Value);
        }

        async Task WireAsync(string name, Guid from, Guid outPort, Guid to, Guid inPort, Guid taken)
        {
            var drawn = await _engine.DeclareAsync(name);
            await _engine.RelateAsync(drawn, @is, wireArchetype);
            await _engine.RelateAsync(from, has, drawn);
            await _engine.RelateAsync(drawn, carries, to);
            await _engine.RelateAsync(drawn, inputConnector, outPort);
            await _engine.RelateAsync(drawn, outputConnector, inPort);
            await _engine.RelateAsync(drawn, takenOn, taken);
        }
        await WireAsync("text to text", uppercase, textOut, reverse, textIn, shout ?? success);
        if (!tellsTheOffice)
        {
            await WireAsync("text to the end", reverse, textOut, end, reached, success);
            return new TextStudio(pipeline, uppercase, reverse, textOut, textIn, shout, loud);
        }

        var firstTell = await _engine.FindAsync("PipelineTell") is null;
        var tellArchetype = await _engine.PlayingAsync(PipelineRoles.PipelineTell, "PipelineTell", isArchetype: true);
        if (firstTell) await _engine.RelateAsync(tellArchetype, @is, nodeArchetype);
        var standsFor = await _engine.PlayingAsync(PipelineRoles.StandsFor, "standsFor", isArchetype: false);
        var toldThrough = await _engine.PlayingAsync(PipelineRoles.ToldThrough, "toldThrough", isArchetype: false);
        var systemArchetype = await _engine.PlayingAsync(PipelineRoles.ExternalSystem, "ExternalSystem", isArchetype: true);
        var office = await _engine.DeclareAsync($"Reporting office {Guid.NewGuid():N}");
        await _engine.RelateAsync(office, @is, systemArchetype);
        var tellsTheOfficeConnection = await _engine.DeclareAsync($"tells the office {Guid.NewGuid():N}");
        await _engine.RelateAsync(tellsTheOfficeConnection, @is, connectionArchetype);
        await _engine.RelateAsync(office, toldThrough, tellsTheOfficeConnection);
        var tell = await _engine.DeclareAsync("The office is told");
        await _engine.RelateAsync(tell, @is, tellArchetype);
        await _engine.RelateAsync(tell, standsFor, office);
        async Task<Guid> TellPortAsync(string direction)
        {
            var port = await _engine.DeclareAsync($"tell.{direction}.text",
                properties: new Dictionary<string, object?> { ["portName"] = "text", ["direction"] = direction, ["type"] = "string" });
            await _engine.RelateAsync(port, @is, portArchetype);
            await _engine.RelateAsync(tell, has, port);
            return port;
        }
        var toldIn = await TellPortAsync("in");
        var toldOut = await TellPortAsync("out");
        await _engine.RelateAsync(pipeline, has, tell);
        await WireAsync("text to the office", reverse, textOut, tell, toldIn, success);
        await WireAsync("told to the end", tell, toldOut, end, reached, success);
        return new TextStudio(pipeline, uppercase, reverse, textOut, textIn, shout, loud, tell, office, tellsTheOfficeConnection);
    }

    private async Task<PipelineDag> ReadAsync(Guid pipeline) => PipelineDagBuilder.Build(await LoadAsync(pipeline), pipeline);

    private async Task<PipelineGraph> LoadAsync(Guid pipeline)
    {
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, new ModelClock(), new LaunchToken(_engine.AdminToken));
        return await gateway.LoadPipelineSubgraphAsync(pipeline, CancellationToken.None);
    }

    [FactAgainstTheEngine]
    public async Task Loading_one_pipeline_carries_none_of_another_pipelines_ports_or_wires()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: false);
        var other = await DrawTextStudioAsync(uppercaseChooses: false);

        var graph = await LoadAsync(studio.Pipeline);

        var carried = graph.Things.Select(thing => thing.Id).ToHashSet();
        carried.Should().Contain([studio.TextIn, studio.TextOut]);
        carried.Should().NotContain([other.TextIn, other.TextOut, other.Uppercase, other.Reverse]);
        graph.Things.Count(thing => thing.Name == "text to text").Should().Be(1, "the other pipeline's wire of the same name is not read");
        DagValidator.Validate(PipelineDagBuilder.Build(graph, studio.Pipeline)).Should().BeEmpty();
    }

    [FactAgainstTheEngine]
    public async Task A_services_ports_declared_on_the_prototype_it_is_are_read()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: false, portsOnAPrototype: true);

        var dag = await ReadAsync(studio.Pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        dag.Node(studio.Uppercase)!.Ports.Select(port => port.Id).Should().Contain([studio.TextIn, studio.TextOut]);
    }

    [FactAgainstTheEngine]
    public async Task A_box_drawn_with_no_settings_is_called_with_the_callParams_its_service_holds()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: false, transformCallParams: """{"step":"uppercase"}""");

        var dag = await ReadAsync(studio.Pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        dag.Node(studio.Uppercase)!.Params.Should().ContainKey("step").WhoseValue.GetString().Should().Be("uppercase");
    }

    [FactAgainstTheEngine]
    public async Task A_wire_from_an_out_port_named_like_an_in_port_of_the_same_service_is_read_through_its_connectors()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: false);

        var dag = await ReadAsync(studio.Pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        var read = dag.WiresOutOf(studio.Uppercase).Should().ContainSingle().Subject;
        (read.From.Node.NodeId, read.From.Port.Id, read.To.Node.NodeId, read.To.Port.Id)
            .Should().Be((studio.Uppercase, studio.TextOut, studio.Reverse, studio.TextIn));
    }

    [FactAgainstTheEngine]
    public async Task A_nodes_choice_is_read_with_its_rule_and_the_Things_it_compares_with()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: true);

        var dag = await ReadAsync(studio.Pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        var choice = dag.Node(studio.Uppercase)!.DecisionPaths.Should().ContainSingle().Subject;
        (choice.Id, choice.When is not null, choice.Things.GetRawText())
            .Should().Be((studio.Shout!.Value, true, $"[{{\"id\":\"{studio.Loud}\",\"name\":\"loud\"}}]"));
    }

    [FactAgainstTheEngine]
    public async Task A_tell_node_is_read_as_one_with_the_system_it_stands_for_and_the_connection_that_system_is_told_through()
    {
        var studio = await DrawTextStudioAsync(uppercaseChooses: false, tellsTheOffice: true);

        var dag = await ReadAsync(studio.Pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        var tell = dag.Node(studio.Tell!.Value)!;
        tell.Kind.Should().Be(DagNodeKind.Tell);
        (tell.Tells!.SystemId, tell.Tells.ConnectionId).Should().Be((studio.Office!.Value, studio.TellsTheOffice!.Value));
    }
}
