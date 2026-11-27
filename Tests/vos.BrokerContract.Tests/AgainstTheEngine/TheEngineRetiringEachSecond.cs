using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The broker's retirement pass otherwise runs once a minute, longer than a case may wait.
public sealed class TheEngineRetiringEachSecond : IAsyncLifetime
{
    private readonly string _seed = RunRetirementSeed.Write();

    public TheEngineRetiringEachSecond() => Engine = new() { RetiresEachSecond = true, SeedFile = _seed };

    public TheEngine Engine { get; }

    public async Task InitializeAsync()
    {
        await Engine.InitializeAsync();
        File.Delete(_seed);
    }

    public Task DisposeAsync() => Engine.DisposeAsync();
}
