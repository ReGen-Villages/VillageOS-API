using vos.Tests.Shared;

namespace vos.Service.ModelBridge.Tests;

public class ModelBridgeWebApplicationFactory : ServiceWebApplicationFactory<Program>;

public class StartupTests : NodeServiceStartupTests<ModelBridgeWebApplicationFactory, Program>
{
    protected override string ServiceName => "ModelBridge";
}

public class AuthWireupTests : AuthWiringTests<ModelBridgeWebApplicationFactory, Program>
{
    protected override string Audience => "modelbridge-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { runId = Guid.NewGuid(), nodeId = Guid.NewGuid() }),
        ("/shutdown", null),
    ];
}
