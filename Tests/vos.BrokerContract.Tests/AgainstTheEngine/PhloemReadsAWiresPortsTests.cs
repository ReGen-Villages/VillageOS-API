using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Phloem.Services;
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
        await PlayingAsync(PipelineRoles.PipelineOutput, "PipelineOutput");
        var connectionArchetype = await PlayingAsync(PipelineRoles.Connection, "PlatformServiceConnection");
        var serviceArchetype = await PlayingAsync(PipelineRoles.Service, "Service");
        var portArchetype = await PlayingAsync(PipelineRoles.Port, "Port");
        var wireArchetype = await PlayingAsync(PipelineRoles.PipelineWire, "PipelineWire");
        var inputConnector = await PlayingAsync(PipelineRoles.InputConnector, "inputConnector", isArchetype: false);
        var outputConnector = await PlayingAsync(PipelineRoles.OutputConnector, "outputConnector", isArchetype: false);
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

        var wire = await _engine.DeclareAsync("text to text");
        await _engine.RelateAsync(wire, @is, wireArchetype);
        await _engine.RelateAsync(uppercase, has, wire);
        await _engine.RelateAsync(wire, carries, reverse);
        await _engine.RelateAsync(wire, inputConnector, textOut);
        await _engine.RelateAsync(wire, outputConnector, textIn);

        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);
        var dag = PipelineDagBuilder.Build(await gateway.LoadPipelineSubgraphAsync(pipeline, CancellationToken.None), pipeline);

        DagValidator.Validate(dag).Should().BeEmpty();
        var read = dag.Wires.Should().ContainSingle().Subject;
        (read.From.Node.NodeId, read.From.Port.Id, read.To.Node.NodeId, read.To.Port.Id).Should().Be((uppercase, textOut, reverse, textIn));
    }
}
