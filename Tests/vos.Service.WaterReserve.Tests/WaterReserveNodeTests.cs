using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.WaterReserve.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.WaterReserve.Tests;

public class WaterReserveNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";
    private static readonly Guid Run = Guid.Parse("58050000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("58050000-0000-0000-0000-0000000000bb");

    [Fact]
    public async Task HandleNodeAsync_computes_days_of_supply_and_pct()
    {
        var node = BuildNode();
        var root = Envelope("""{"population":1000,"perCapitaConsumptionM3":50,"storageCapacityM3":100000}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeTrue();
        ((double)response.Outputs["daysOfSupply"]!).Should().BeApproximately(730.0, 1e-6);
        ((double)response.Outputs["pctAnnualConsumption"]!).Should().BeApproximately(200.0, 1e-9);
        ((double)response.Outputs["emergencyReserveM3"]!).Should().Be(100000);
    }

    // #6549: present-but-null is not absent, and reading it straight through named no port.
    [Fact]
    public async Task HandleNodeAsync_null_input_names_the_port()
    {
        var node = BuildNode();
        var root = Envelope("""{"population":1000,"perCapitaConsumptionM3":50,"storageCapacityM3":null}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("storageCapacityM3");
    }

    [Fact]
    public async Task HandleNodeAsync_missing_input_reports_failure_not_exception()
    {
        var node = BuildNode();
        var root = Envelope("""{"population":1000,"perCapitaConsumptionM3":50}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("storageCapacityM3");
        response.Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Ports_advertise_the_three_inputs_and_days_of_supply_output()
    {
        var node = BuildNode();

        node.Ports.Should().ContainSingle(p => p.PortName == "population" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "storageCapacityM3" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "daysOfSupply" && p.Direction == "out");
    }

    private static WaterReserveNode BuildNode()
    {
        // Literal inputs need no ref resolution, so no HTTP is expected.
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP expected"));
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        return new WaterReserveNode(factory, NullLogger<WaterReserveNode>.Instance, MyceliumUrl, ServiceToken);
    }

    private static JsonElement Envelope(string inputs) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{{inputs}}}""").RootElement;
}
