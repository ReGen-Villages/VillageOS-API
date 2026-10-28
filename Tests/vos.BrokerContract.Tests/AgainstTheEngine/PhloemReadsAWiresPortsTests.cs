using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The orchestrator's unit tests hand it a graph they built, so only the real engine shows that the drawing read
// brings each wire's two connector relationships and the ports they reach.
public class PhloemReadsAWiresPortsTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsAWiresPortsTests(TheEngine engine) => _engine = engine;

    private async Task<Guid> PlayingAsync(Role role, string name, bool isArchetype = true)
    {
        var played = await _engine.DeclareAsync(name, isArchetype);
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), played);
        return played;
    }

    [FactAgainstTheEngine]
    public async Task A_wire_from_an_out_port_named_like_an_in_port_of_the_same_service_is_read_through_its_connectors()
    {
        var @is = await _engine.DeclareAsync(ModelNames.Is);
        var has = await _engine.DeclareAsync(ModelNames.Has);
        await _engine.DeclareAsync(ModelNames.Binds);
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
        await PlayingAsync(PipelineRoles.DecisionPath, "DecisionPath");
        var carries = await _engine.DeclareAsync("carries");
        await _engine.RelateAsync(carries, @is, wireArchetype);

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
        var errorPort = await _engine.DeclareAsync("error",
            properties: new Dictionary<string, object?> { ["portName"] = "error", ["direction"] = "out", ["type"] = "string" });
        await _engine.RelateAsync(errorPort, @is, portArchetype);
        await _engine.DeclareAsync($"{PipelineRoles.ErrorPort.Word} role", identifier: PipelineRoles.ErrorPort.Id);
        await _engine.RelateAsync(PipelineRoles.ErrorPort.Id, await _engine.TheOnlyThingNamedAsync(ModelNames.Binds), errorPort);

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

        await _engine.RelateAsync(outputArchetype, @is, nodeArchetype);
        var end = await _engine.DeclareAsync("Text Studio end");
        await _engine.RelateAsync(end, @is, outputArchetype);
        var reached = await _engine.DeclareAsync("end.text",
            properties: new Dictionary<string, object?> { ["portName"] = "text", ["direction"] = "in", ["type"] = "string" });
        await _engine.RelateAsync(reached, @is, portArchetype);
        await _engine.RelateAsync(end, has, reached);
        await _engine.RelateAsync(pipeline, has, end);

        async Task<Guid> WireAsync(string name, Guid from, Guid outPort, Guid to, Guid inPort)
        {
            var drawn = await _engine.DeclareAsync(name);
            await _engine.RelateAsync(drawn, @is, wireArchetype);
            await _engine.RelateAsync(from, has, drawn);
            await _engine.RelateAsync(drawn, carries, to);
            await _engine.RelateAsync(drawn, inputConnector, outPort);
            await _engine.RelateAsync(drawn, outputConnector, inPort);
            await _engine.RelateAsync(drawn, takenOn, success);
            return drawn;
        }
        await WireAsync("text to text", uppercase, textOut, reverse, textIn);
        await WireAsync("text to the end", reverse, textOut, end, reached);

        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);
        var dag = PipelineDagBuilder.Build(await gateway.LoadPipelineSubgraphAsync(pipeline, CancellationToken.None), pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        var read = dag.WiresOutOf(uppercase).Should().ContainSingle().Subject;
        (read.From.Node.NodeId, read.From.Port.Id, read.To.Node.NodeId, read.To.Port.Id).Should().Be((uppercase, textOut, reverse, textIn));
    }
}
