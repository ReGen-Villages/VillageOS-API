using vos.Tests.Shared;

namespace vos.Service.EnergyBalance.Tests;

public class EnergyBalanceWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<EnergyBalanceWebApplicationFactory, Program>
{
    protected override string ServiceName => "EnergyBalance";
}

public class AuthWireupTests : AuthWiringTests<EnergyBalanceWebApplicationFactory, Program>
{
    protected override string Audience => "energybalance-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
