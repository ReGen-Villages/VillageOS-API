using System;
using System.IO;
using System.Linq;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

/// <summary>
/// A service that parses its own /handle body raises on text that is not JSON, and the caller gets a
/// failed request where it should have had a refusal. Worse, a failed dispatch is re-driven: a body
/// that can never parse is retried rather than refused once. The shared classifier answers
/// <c>Unrecognised</c> for such text, so a service hands it the body and never parses one itself.
/// </summary>
public class ServicesDoNotParseHandleBodiesTests
{
    private const string ParsingTheBodyIntoAnElement = "JsonSerializer.Deserialize<JsonElement>";

    [Fact]
    public void No_service_entry_point_parses_its_own_request_body()
    {
        var root = RepositoryRoot.Find();

        var parsing = ServiceEntryPoints.Under(root)
            .Where(entryPoint => Unqualified(File.ReadAllText(entryPoint))
                .Contains(ParsingTheBodyIntoAnElement, StringComparison.Ordinal))
            .Select(entryPoint => Path.GetRelativePath(root, entryPoint))
            .ToList();

        Assert.True(parsing.Count == 0,
            "these service entry points parse a request body themselves, so a body that is not JSON "
            + "raises instead of being refused — hand the text to HandleRequestRouter.Classify instead: "
            + string.Join(", ", parsing));
    }

    private static string Unqualified(string source) =>
        source.Replace("System.Text.Json.", string.Empty, StringComparison.Ordinal);
}
