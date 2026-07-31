using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.EnergyBalance.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.EnergyBalance.Tests;

public class EnergyBalanceNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "svc-jwt-abc";
    private static readonly Guid Run = Guid.Parse("58060000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("58060000-0000-0000-0000-0000000000bb");

    [Fact]
    public async Task HandleNodeAsync_computes_generation_and_net_positive()
    {
        var node = BuildNode();
        var root = Envelope("""
            {"solarPvAreaM2":100000,"solarResourceKwhPerM2PerYear":1000,"pvEfficiency":0.20,
             "otherGenerationMwhPerYear":900,"annualConsumptionMwhPerYear":18743}
            """);

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeTrue();
        ((double)response.Outputs["totalGenerationMwhPerYear"]!).Should().BeApproximately(20900, 1e-6);
        ((bool)response.Outputs["netPositive"]!).Should().BeTrue();
    }

    [Fact]
    public async Task HandleNodeAsync_missing_input_reports_failure_not_exception()
    {
        var node = BuildNode();
        var root = Envelope("""
            {"solarPvAreaM2":100000,"solarResourceKwhPerM2PerYear":1000,"pvEfficiency":0.20,
             "otherGenerationMwhPerYear":900}
            """);

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("annualConsumptionMwhPerYear");
        response.Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Ports_advertise_generation_inputs_and_net_positive_output()
    {
        var node = BuildNode();

        node.Ports.Should().ContainSingle(p => p.PortName == "solarPvAreaM2" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "annualConsumptionMwhPerYear" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "netPositive" && p.Direction == "out");
    }

    private static EnergyBalanceNode BuildNode()
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP expected"));
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        return new EnergyBalanceNode(factory, NullLogger<EnergyBalanceNode>.Instance, MyceliumUrl, ServiceToken);
    }

    private static JsonElement Envelope(string inputs) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{{inputs}}}""").RootElement;
}
