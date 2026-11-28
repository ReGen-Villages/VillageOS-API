using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The test host runs the broker's retirement pass only when given an interval, and the broker's own minute is longer than a case may wait.
public sealed class TheEngineRetiringEachSecond : IAsyncLifetime
{
    private readonly string _seed = RunRetirementSeed.Write();

    public TheEngineRetiringEachSecond() => Engine = new() { RetirementPassInterval = TimeSpan.FromSeconds(1), SeedFile = _seed };

    public TheEngine Engine { get; }

    public async Task InitializeAsync()
    {
        await Engine.InitializeAsync();
        File.Delete(_seed);
    }

    public Task DisposeAsync() => Engine.DisposeAsync();
}
