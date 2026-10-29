using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The orchestrator's unit tests hand it a graph they built, so only the real engine shows that the drawing read
// brings each wire's two connector relationships and the ports they reach, and each node's choices with the Things
// they compare with.
public class PhloemReadsAWiresPortsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsAWiresPortsTests(TheEngine engine) => _engine = engine;

    // Both drawings share one engine, so the vocabulary is declared and bound once.
    private async Task<Guid> PlayingAsync(Role role, string name, bool isArchetype = true, Dictionary<string, object?>? properties = null)
    {
        var played = await _engine.DeclareOnceAsync(name, isArchetype, properties);
        if (await _engine.FindAsync($"{role.Word} role") is not null) return played;
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
        return played;
    }

    private sealed record TextStudio(Guid Pipeline, Guid Uppercase, Guid Reverse, Guid TextOut, Guid TextIn, Guid? Shout, Guid? Loud,
        Guid? Tell = null, Guid? Office = null, Guid? TellsTheOffice = null);

    private async Task<TextStudio> DrawTextStudioAsync(bool uppercaseChooses, bool tellsTheOffice = false)
    {
        var firstDrawing = await _engine.FindAsync(ModelNames.Is) is null;
        var @is = await _engine.DeclareOnceAsync(ModelNames.Is);
        var has = await _engine.DeclareOnceAsync(ModelNames.Has);
        await _engine.DeclareOnceAsync(ModelNames.Binds);
        var pipelineArchetype = await PlayingAsync(PipelineRoles.Pipeline, "Pipeline");
        var nodeArchetype = await PlayingAsync(PipelineRoles.PipelineNode, "PipelineNode");
        await PlayingAsync(PipelineRoles.PipelineInput, "PipelineInput");
        var outputArchetype = await PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput");
        var connectionArchetype = await PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection");
        var serviceArchetype = await PlayingAsync(PipelineRoles.Service, "Service");
        var portArchetype = await PlayingAsync(PipelineRoles.Port, "Port");
        var wireArchetype = await PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire");
        var inputConnector = await PlayingAsync(PipelineRoles.InputConnector, "inputConnector", isArchetype: false);
        var outputConnector = await PlayingAsync(PipelineRoles.OutputConnector, "outputConnector", isArchetype: false);
        var takenOn = await PlayingAsync(PipelineRoles.TakenOn, "takenOn", isArchetype: false);
        var success = await PlayingAsync(PipelineRoles.Success, "success", isArchetype: false);
        await PlayingAsync(PipelineRoles.Fail, "fail", isArchetype: false);
        var decisionPath = await PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath");
        var comparesWith = await PlayingAsync(PipelineRoles.ComparesWith, "comparesWith", isArchetype: false);
        var carries = await _engine.DeclareOnceAsync("carries");
        if (firstDrawing) await _engine.RelateAsync(carries, @is, wireArchetype);

        var transform = await _engine.DeclareAsync("Transform service");
        await _engine.RelateAsync(transform, @is, serviceArchetype);
        async Task<Guid> PortAsync(string direction)
        {
            var port = await _engine.DeclareAsync($"transform.{direction}.text",
                properties: new Dictionary<string, object?> { ["portName"] = "text", ["direction"] = direction, ["type"] = "string" });
            await _engine.RelateAsync(port, @is, portArchetype);
            await _engine.RelateAsync(transform, has, port);
            return port;
        }
        var textIn = await PortAsync("in");
        var textOut = await PortAsync("out");
        var errorPort = await PlayingAsync(PipelineRoles.ErrorPort, "error", isArchetype: false,
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
        var tellArchetype = await PlayingAsync(PipelineRoles.PipelineTell, "PipelineTell");
        if (firstTell) await _engine.RelateAsync(tellArchetype, @is, nodeArchetype);
        var standsFor = await PlayingAsync(PipelineRoles.StandsFor, "standsFor", isArchetype: false);
        var toldThrough = await PlayingAsync(PipelineRoles.ToldThrough, "toldThrough", isArchetype: false);
        var systemArchetype = await PlayingAsync(PipelineRoles.ExternalSystem, "ExternalSystem");
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

    private async Task<PipelineDag> ReadAsync(Guid pipeline)
    {
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);
        return PipelineDagBuilder.Build(await gateway.LoadPipelineSubgraphAsync(pipeline, CancellationToken.None), pipeline);
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
