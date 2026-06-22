// EchoNode is the reference pipeline DAG node (Feature #5628, #5631): it copies its "message" input
// to its "echo" output through the shared DagNodeService envelope. These tests pin that behaviour and
// the advertised port manifest.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Echo.Services;
using vos.ManagedMicroservice.Shared.DagNode;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Echo.Tests;

public class EchoNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";
    private static readonly Guid Run = Guid.Parse("5628da90-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("5628da90-0000-0000-0000-0000000000bb");

    [Fact]
    public async Task HandleNodeAsync_CopiesMessageInputToEchoOutput()
    {
        var node = BuildNode();
        var root = Envelope("""{"message":"hello pipeline"}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeTrue();
        response.Outputs["echo"].Should().Be("hello pipeline");
    }

    [Fact]
    public async Task HandleNodeAsync_MissingMessage_ReportsFailureNotException()
    {
        var node = BuildNode();
        var root = Envelope("""{}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("message");
        response.Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Ports_AdvertiseRequiredMessageInAndEchoOut()
    {
        var node = BuildNode();

        node.Ports.Should().ContainSingle(p => p.PortName == "message" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "echo" && p.Direction == "out");
    }

    private static EchoNode BuildNode()
    {
        // No HTTP is expected: literal inputs need no ref resolution.
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP expected"));
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        return new EchoNode(factory, NullLogger<EchoNode>.Instance, MyceliumUrl, ServiceToken);
    }

    private static JsonElement Envelope(string inputs) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{{inputs}}}""").RootElement;
}
