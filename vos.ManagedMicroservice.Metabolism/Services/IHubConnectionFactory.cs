using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR.Client;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// Minimal abstraction over the SignalR <see cref="HubConnection"/> surface that
/// <see cref="BrokerClient.ConnectSignalRAsync"/> actually uses. SignalR ships
/// <c>HubConnection</c> as a sealed class with no public interface, so the connect
/// loop is untestable against a real hub; this seam lets tests substitute a fake
/// that records handler registrations and fires events synchronously.
/// </summary>
public interface IHubConnection : IAsyncDisposable
{
    /// <summary>Registers a handler for a three-argument hub method.</summary>
    void On<T1, T2, T3>(string methodName, Action<T1, T2, T3> handler);

    /// <summary>Raised when the connection re-establishes after a drop.</summary>
    event Func<string?, Task>? Reconnected;

    /// <summary>Starts the connection.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates <see cref="IHubConnection"/> instances for a given hub URL and token provider.</summary>
public interface IHubConnectionFactory
{
    IHubConnection Create(string url, Func<Task<string?>> accessTokenProvider);
}

/// <summary>
/// Production factory: builds a real SignalR <see cref="HubConnection"/> with automatic
/// reconnect and wraps it in <see cref="DefaultHubConnection"/>. This is the only place
/// in the Metabolism service that constructs a <see cref="HubConnectionBuilder"/>.
/// </summary>
/// <remarks>
/// Excluded from coverage: a pass-through to the sealed, un-mockable SignalR
/// <see cref="HubConnection"/>. This is the irreducible seam the abstraction exists to
/// isolate — the testable retry/routing logic lives in <see cref="BrokerClient"/>.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class DefaultHubConnectionFactory : IHubConnectionFactory
{
    public IHubConnection Create(string url, Func<Task<string?>> accessTokenProvider) =>
        new DefaultHubConnection(
            new HubConnectionBuilder()
                .WithUrl(url, options => options.AccessTokenProvider = accessTokenProvider)
                .WithAutomaticReconnect()
                .Build());
}

/// <summary>Thin pass-through wrapper that adapts a real <see cref="HubConnection"/> to <see cref="IHubConnection"/>.</summary>
[ExcludeFromCodeCoverage]
internal sealed class DefaultHubConnection : IHubConnection
{
    private readonly HubConnection _inner;

    public DefaultHubConnection(HubConnection inner) => _inner = inner;

    public void On<T1, T2, T3>(string methodName, Action<T1, T2, T3> handler) =>
        _inner.On(methodName, handler);

    public event Func<string?, Task>? Reconnected
    {
        add => _inner.Reconnected += value;
        remove => _inner.Reconnected -= value;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _inner.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
