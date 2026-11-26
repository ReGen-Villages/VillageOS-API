using vos.Tests.Shared;

namespace vos.Service.Watershed.Tests;

public class WatershedWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<WatershedWebApplicationFactory, Program>
{
    protected override string ServiceName => "Watershed";
}

public class AuthWireupTests : AuthWiringTests<WatershedWebApplicationFactory, Program>
{
    protected override string Audience => "watershed-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
