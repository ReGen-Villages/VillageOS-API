using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// Why an analysis did not start. A run answers its dispatch before it fetches, so this is no longer
// carried in a response body — it is the spawner's own answer, and it is asked for here rather than
// through the endpoint, which now has nothing to say about it.
//
// "Did not start" alone is unactionable: a planner reading a blank balance has to be able to tell a
// model that declares no compute connection from a site that has no study from a write that failed.
public class AnalysisSpawnerTests
{
    private static AnalysisSpawner Spawner() =>
        new(new MyceliumRelationshipClient(
                new UnreachableHttpClientFactory(), NullLogger<MyceliumRelationshipClient>.Instance,
                "http://localhost"),
            NullLogger<AnalysisSpawner>.Instance);

    // Neither case below reaches the network; the factory exists to prove that, by failing if one does.
    private sealed class UnreachableHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("nothing here should reach the model");
    }

    [Fact]
    public async Task ASiteWithNoStudy_StartsNothingAndSaysSo()
    {
        var spawn = await Spawner().SpawnAsync(Guid.NewGuid(), analysis: null, CancellationToken.None);

        spawn.Started.Should().BeFalse();
        spawn.Reason.Should().Contain("no study");
    }

    [Fact]
    public async Task AModelMarkingNoConnection_StartsNothingAndSaysSo()
    {
        var analysis = new SiteAnalysis(Guid.NewGuid(), Array.Empty<AnalysisTrigger>());

        var spawn = await Spawner().SpawnAsync(Guid.NewGuid(), analysis, CancellationToken.None);

        spawn.Started.Should().BeFalse();
        spawn.Reason.Should().Contain("marks no connection",
            "a model seeded without its compute connections is a gap in the model, not a discovery that failed");
    }
}
