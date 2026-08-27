using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// A service reaches the broker with whatever credential it was built holding. Every launch reads an
/// API key, and a service that reads one and then builds its clients without it authenticates as
/// nobody — which the broker answers with a refusal the service reports as its own failure.
///
/// Each construction is judged rather than the file: a service can build one client correctly and
/// another beside it without the key, and a file-wide search for the word would call that good.
/// </summary>
public class ServicesPresentTheirApiKeyTests
{
    [Fact]
    public void Every_broker_client_a_service_builds_is_given_the_key_the_service_holds()
    {
        var root = RepositoryRoot.Find();
        var clientTypes = BrokerClientTypesUnder(root);

        var withoutTheKey =
            from entryPoint in ServiceEntryPoints.Under(root)
            let source = File.ReadAllText(entryPoint)
            from clientType in clientTypes
            from construction in ConstructionsOf(clientType, source)
            where !construction.Contains("apiKey") && !construction.Contains("ApiKey")
            select $"{Path.GetRelativePath(root, entryPoint)} builds {clientType}";

        var found = withoutTheKey.ToList();

        Assert.True(found.Count == 0,
            "these clients are built without the API key their service holds, so they present no "
            + $"credential and every call they make is refused: {string.Join(", ", found)}");
    }

    /// <summary>The client types a service reaches the broker through: everything deriving from the base
    /// that holds a credential. Read from the checkout rather than listed here, so a new client is
    /// covered on the day it is written.
    ///
    /// The parameter list between the name and the base is optional because a client written with a
    /// primary constructor carries one — and the client this guard was written for is one of those.
    /// </summary>
    private static IReadOnlyCollection<string> BrokerClientTypesUnder(string root)
    {
        var derived = new HashSet<string> { "SubscriptionClient" };

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match match in Regex.Matches(
                         File.ReadAllText(file),
                         @"class\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*(?:MyceliumClientBase|SubscriptionClient)\b"))
                derived.Add(match.Groups[1].Value);
        }

        return derived;
    }

    /// <summary>Every <c>new T(...)</c> in the source, each as the whole call. Taken by balancing the
    /// brackets rather than by reading to the next one: these calls hold nested calls of their own, and
    /// stopping at the first closing bracket would cut every one of them short.</summary>
    private static IEnumerable<string> ConstructionsOf(string clientType, string source)
    {
        foreach (Match match in Regex.Matches(source, @"new\s+" + Regex.Escape(clientType) + @"\s*\("))
        {
            var index = match.Index + match.Length - 1;
            var depth = 0;

            for (var scan = index; scan < source.Length; scan++)
            {
                if (source[scan] == '(') depth++;
                else if (source[scan] == ')' && --depth == 0)
                {
                    yield return source[match.Index..(scan + 1)];
                    break;
                }
            }
        }
    }
}
