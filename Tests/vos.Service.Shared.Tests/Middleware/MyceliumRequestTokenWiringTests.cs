using FluentAssertions;
using Xunit;

namespace vos.Service.Shared.Tests.Middleware;

// A service reached through /handle must adopt the bearer that arrived, or MyceliumClientBase falls
// back to the daemon's startup token and the service reads and writes in whichever model launched it.
// Omitting the middleware breaks nothing at run time with a single model loaded, so this pins the
// convention across every entry point rather than the behaviour of any one service.
public class MyceliumRequestTokenWiringTests
{
    private const string HandleRoute = "\"/handle\"";
    private const string Middleware = "UseMyceliumRequestToken";

    [Fact]
    public void EveryServiceExposingHandle_InstallsTheRequestTokenMiddleware()
    {
        var exposingHandle = ServiceEntryPoints()
            .Where(entryPoint => entryPoint.Source.Contains(HandleRoute))
            .ToList();

        exposingHandle.Should().NotBeEmpty(
            "services exposing /handle must be discoverable, otherwise this test passes without checking anything");

        var missing = exposingHandle
            .Where(entryPoint => !entryPoint.Source.Contains(Middleware))
            .Select(entryPoint => entryPoint.ServiceName)
            .ToList();

        missing.Should().BeEmpty(
            "a service exposing /handle that never calls {0} sends its own startup token to the broker instead of the caller's",
            Middleware);
    }

    private record ServiceEntryPoint(string ServiceName, string Source);

    private static IReadOnlyList<ServiceEntryPoint> ServiceEntryPoints() =>
        Directory.GetDirectories(RepositoryRoot(), "vos.Service.*")
            .Select(directory => new { Directory = directory, EntryPoint = Path.Combine(directory, "Program.cs") })
            .Where(service => File.Exists(service.EntryPoint))
            .OrderBy(service => service.Directory)
            .Select(service => new ServiceEntryPoint(
                Path.GetFileName(service.Directory),
                File.ReadAllText(service.EntryPoint)))
            .ToList();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VillageOS-API.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find VillageOS-API.sln above " + AppContext.BaseDirectory);
    }
}
