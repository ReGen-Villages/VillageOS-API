using vos.Tests.Shared;

namespace vos.Service.WaterReserve.Tests;

public class WaterReserveWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<WaterReserveWebApplicationFactory, Program>
{
    protected override string ServiceName => "WaterReserve";
}

public class AuthWireupTests : AuthWiringTests<WaterReserveWebApplicationFactory, Program>
{
    protected override string Audience => "waterreserve-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
