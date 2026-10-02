using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The console asks the address that served it for more than its own files, and its development server
// forwards each of those paths. The reference Caddyfile answers every path it does not forward with the
// console's page, so a path forwarded in development and not there is answered 200 with HTML and nothing
// reports a fault.
public class ReferenceProxyForwardsWhatTheConsoleAsksForTests
{
    // The broker serves its interface description in development alone.
    private static readonly string[] ServedOnlyInDevelopment = ["/swagger"];

    private static readonly string Repository = RepositoryRoot.Find();

    [Fact]
    public void Every_path_the_development_server_forwards_is_forwarded_to_the_same_port()
    {
        var inDevelopment = ForwardedInDevelopment();
        Assert.Contains("/api", inDevelopment.Keys);

        var behindTheProxy = ForwardedByTheReferenceProxy();
        var answeredWithTheConsolesPage = inDevelopment
            .Where(forwarded => !ServedOnlyInDevelopment.Contains(forwarded.Key))
            .Where(forwarded => behindTheProxy.GetValueOrDefault(forwarded.Key) != forwarded.Value)
            .Select(forwarded => $"{forwarded.Key} (port {forwarded.Value})");

        Assert.Empty(answeredWithTheConsolesPage);
    }

    private static Dictionary<string, string> ForwardedInDevelopment() =>
        Regex.Matches(
                File.ReadAllText(Path.Combine(Repository, "vos.Trellis", "vite.config.ts")),
                @"'(?<path>/[a-z]+)':\s*\{\s*target:\s*'https?://localhost:(?<port>\d+)'")
            .ToDictionary(match => match.Groups["path"].Value, match => match.Groups["port"].Value);

    private static Dictionary<string, string> ForwardedByTheReferenceProxy() =>
        Regex.Matches(
                File.ReadAllText(Path.Combine(Repository, "deploy", "Caddyfile")),
                @"handle(?:_path)? (?<path>/[a-z]+)/\* \{\s*reverse_proxy localhost:(?<port>\d+)")
            .ToDictionary(match => match.Groups["path"].Value, match => match.Groups["port"].Value);
}
