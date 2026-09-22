using System.Text.RegularExpressions;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Middleware;

// A service reached through /handle must adopt the bearer that arrived, or MyceliumClientBase falls
// back to the daemon's startup token and the service reads and writes in whichever model launched it.
// Omitting the middleware breaks nothing at run time with a single model loaded, so this pins the
// convention across every service rather than the behaviour of any one of them.
public class MyceliumModelTokenWiringTests
{
    private const string HandleRoute = "\"/handle\"";
    private const string Middleware = "UseMyceliumModelToken";

    [Fact]
    public void EveryServiceExposingHandle_InstallsTheModelTokenMiddleware()
    {
        var exposingHandle = Services()
            .Where(service => service.WholeServiceSource.Contains(HandleRoute))
            .ToList();

        exposingHandle.Should().NotBeEmpty(
            "services exposing /handle must be discoverable, otherwise this test passes without checking anything");

        var missing = exposingHandle
            .Where(service => !service.EntryPointSource.Contains(Middleware))
            .Select(service => service.ServiceName)
            .ToList();

        missing.Should().BeEmpty(
            "a service exposing /handle that never calls {0} sends its own startup token to the broker instead of the caller's",
            Middleware);
    }

    // The route is looked for across the whole service, because a service may map it from a helper
    // rather than inline — Metabolism maps /handle in Endpoints/EndpointMapper.cs. The middleware is
    // looked for in the entry point, which is where every service builds its request pipeline.
    private record ServiceUnderTest(string ServiceName, string EntryPointSource, string WholeServiceSource);

    private static IReadOnlyList<ServiceUnderTest> Services() =>
        Directory.GetDirectories(RepositoryRoot.Find(), "vos.Service.*")
            .Select(directory => new { Directory = directory, EntryPoint = Path.Combine(directory, "Program.cs") })
            .Where(service => File.Exists(service.EntryPoint))
            .OrderBy(service => service.Directory)
            .Select(service => new ServiceUnderTest(
                Path.GetFileName(service.Directory),
                WithoutComments(File.ReadAllText(service.EntryPoint)),
                WholeServiceSourceOf(service.Directory)))
            .ToList();

    private static string WholeServiceSourceOf(string directory) =>
        string.Join('\n', Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file)
            .Select(file => WithoutComments(File.ReadAllText(file))));

    // Commenting the call out would otherwise satisfy a plain text search, which is the likeliest way
    // for the wiring to disappear. The lookbehind keeps "https://" in a string literal from being read
    // as the start of a comment.
    private static string WithoutComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return string.Join('\n', withoutBlockComments
            .Split('\n')
            .Select(line => Regex.Replace(line, @"(?<!:)//.*$", string.Empty)));
    }
}
