using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.EnergyBalance.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.EnergyBalance.Tests;

public class EnergyBalancePortsTests
{
    // The in port and the calculator's parameter are one name, so no step between them can reinterpret a figure.
    // The unit is in the name, which is what makes a move from megawatt-hours to kilowatt-hours a rename both
    // sides have to follow rather than a silent factor of a thousand.
    [Fact]
    public void Each_figure_the_node_takes_in_is_one_the_calculator_takes_by_the_same_name()
    {
        var node = new EnergyBalanceNode(new PerCallHttpClientFactory(new HttpClientHandler()), NullLogger<EnergyBalanceNode>.Instance,
            "http://localhost", new LaunchToken("token"));

        node.Ports.Where(port => port.Direction == "in").Select(port => port.PortName)
            .Should().BeEquivalentTo(TakenBy<EnergyBalanceInputs>());
    }

    // A calculator takes its figures as a record, so the parameter names are the port names with an upper initial.
    private static IEnumerable<string> TakenBy<TInputs>() =>
        typeof(TInputs).GetConstructors().Single().GetParameters()
            .Select(parameter => char.ToLowerInvariant(parameter.Name![0]) + parameter.Name[1..]);
}
