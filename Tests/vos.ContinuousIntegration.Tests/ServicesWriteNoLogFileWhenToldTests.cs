using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// A service the broker runs as its own user may write only to standard output, which its unit captures; one that
// ignored --logToFile=false would write into a folder only root can read.
public class ServicesWriteNoLogFileWhenToldTests
{
    private static readonly Regex LoggingSetUp = new(@"ServiceHost\.ConfigureLogging\((?<arguments>[^;]*)\);");

    [Fact]
    public void Bug8495_Every_service_sets_up_its_logging_through_the_service_host_and_passes_LogToFile()
    {
        var root = RepositoryRoot.Find();
        var entryPoints = ServiceEntryPoints.Under(root).ToList();

        var problems =
            from entryPoint in entryPoints
            let calls = LoggingSetUp.Matches(ServiceEntryPoints.SourceOf(root, entryPoint))
            let name = Path.GetRelativePath(root, entryPoint)
            where calls.Count != 1 || !Regex.IsMatch(calls[0].Groups["arguments"].Value, @"writeToFile:\s*[\w.]*\bLogToFile\b")
            select calls.Count != 1
                ? $"{name} calls ServiceHost.ConfigureLogging {calls.Count} times, not once"
                : $"{name} writes a log file whatever --logToFile says";

        Assert.NotEmpty(entryPoints);
        Assert.Empty(problems);
    }
}
