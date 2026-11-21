using vos.Tests.Shared;

namespace vos.Service.Transect.Tests;

public class TransectWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<TransectWebApplicationFactory, Program>
{
    protected override string ServiceName => "Transect";
}

public class AuthWireupTests : AuthWiringTests<TransectWebApplicationFactory, Program>
{
    protected override string Audience => "transect-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
