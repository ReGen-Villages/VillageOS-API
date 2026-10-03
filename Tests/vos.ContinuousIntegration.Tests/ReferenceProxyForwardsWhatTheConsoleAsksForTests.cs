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

    // The scheme counts as much as the port: the broker answers HTTPS alone, so a proxy reaching it over
    // plain HTTP gets an error for every request, which the console shows as the platform being down.
    [Fact]
    public void Every_path_the_development_server_forwards_is_forwarded_to_the_same_port_with_the_same_scheme()
    {
        var inDevelopment = ForwardedInDevelopment();
        Assert.Contains("/api", inDevelopment.Keys);

        var behindTheProxy = ForwardedByTheReferenceProxy();
        var answeredWrongly = inDevelopment
            .Where(forwarded => !ServedOnlyInDevelopment.Contains(forwarded.Key))
            .Where(forwarded => behindTheProxy.GetValueOrDefault(forwarded.Key) != forwarded.Value)
            .Select(forwarded =>
                $"{forwarded.Key} goes to {forwarded.Value} in development and to "
                + $"{behindTheProxy.GetValueOrDefault(forwarded.Key) ?? "the console's page"} behind the proxy");

        Assert.Empty(answeredWrongly);
    }

    private static Dictionary<string, string> ForwardedInDevelopment() =>
        Regex.Matches(
                File.ReadAllText(Path.Combine(Repository, "vos.Trellis", "vite.config.ts")),
                @"'(?<path>/[a-z]+)':\s*\{\s*target:\s*'(?<upstream>https?://localhost:\d+)'")
            .ToDictionary(match => match.Groups["path"].Value, match => match.Groups["upstream"].Value);

    // A path is forwarded by a handle block naming it, or by one naming a matcher that lists it. An
    // upstream with no scheme is reached over plain HTTP, as Caddy reads it.
    private static Dictionary<string, string> ForwardedByTheReferenceProxy()
    {
        var caddyfile = File.ReadAllText(Path.Combine(Repository, "deploy", "Caddyfile"));
        var pathsByMatcher = Regex.Matches(caddyfile, @"(?<matcher>@\w+) path (?<paths>(?:/[a-z]+/\*[ \t]*)+)")
            .ToDictionary(
                match => match.Groups["matcher"].Value,
                match => Regex.Matches(match.Groups["paths"].Value, @"(?<path>/[a-z]+)/\*").Select(path => path.Groups["path"].Value).ToArray());

        var forwarded = new Dictionary<string, string>();
        foreach (Match handle in Regex.Matches(
                     caddyfile,
                     @"handle(?:_path)? (?:(?<path>/[a-z]+)/\*|(?<matcher>@\w+)) \{\s*reverse_proxy (?:(?<scheme>https?)://)?localhost:(?<port>\d+)"))
        {
            var upstream = $"{(handle.Groups["scheme"].Success ? handle.Groups["scheme"].Value : "http")}://localhost:{handle.Groups["port"].Value}";
            var paths = handle.Groups["path"].Success ? [handle.Groups["path"].Value] : pathsByMatcher[handle.Groups["matcher"].Value];
            foreach (var path in paths) forwarded[path] = upstream;
        }
        return forwarded;
    }
}
