using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The broker's retirement pass otherwise runs once a minute, longer than a case may wait.
public sealed class TheEngineRetiringEachSecond : IAsyncLifetime
{
    public TheEngine Engine { get; } = new() { Settings = new Dictionary<string, string> { ["Retirement:PassInterval"] = "00:00:01" } };

    public Task InitializeAsync() => Engine.InitializeAsync();

    public Task DisposeAsync() => Engine.DisposeAsync();
}
