using vos.Tests.Shared;

namespace vos.Service.Tributary.Tests;

public class AuthWireupTests : AuthWiringTests<TributaryWebApplicationFactory, Program>
{
    protected override string Audience => "tributary-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { endpointName = "X" }),
        ("/shutdown", null),
    ];
}
