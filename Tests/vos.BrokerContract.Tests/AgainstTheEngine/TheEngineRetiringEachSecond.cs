using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The broker's retirement pass otherwise runs once a minute, longer than a case may wait.
public sealed class TheEngineRetiringEachSecond : IAsyncLifetime
{
    public TheEngine Engine { get; } = new() { RetiresEachSecond = true };

    public Task InitializeAsync() => Engine.InitializeAsync();

    public Task DisposeAsync() => Engine.DisposeAsync();
}
