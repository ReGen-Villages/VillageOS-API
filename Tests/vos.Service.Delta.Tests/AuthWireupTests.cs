using vos.Tests.Shared;

namespace vos.Service.Delta.Tests;

public class AuthWireupTests : AuthWiringTests<DeltaWebApplicationFactory, Program>
{
    protected override string Audience => "delta-handler";

    protected override IReadOnlyList<(string Route, object? Body)> GuardedRoutes =>
    [
        ("/handle", new { name = "X", properties = new { url = "x" } }),
        ("/register", new { name = "X", properties = new { url = "x" } }),
        ("/shutdown", null),
    ];
}
