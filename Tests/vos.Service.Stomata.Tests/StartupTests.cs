using vos.Tests.Shared;

namespace vos.Service.Stomata.Tests;

public class StomataWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<StomataWebApplicationFactory, Program>
{
    protected override string ServiceName => "Stomata";
}

public class AuthWireupTests : AuthWiringTests<StomataWebApplicationFactory, Program>
{
    protected override string Audience => "stomata-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
