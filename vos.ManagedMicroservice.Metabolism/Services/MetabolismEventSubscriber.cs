using Microsoft.Extensions.Hosting;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// Bridges Mycelium's relationship-property-changed event to the simulation engine.
/// Wires <see cref="MyceliumClient.OnRelationshipPropertyChanged"/> in <see cref="StartAsync"/>
/// and removes it in <see cref="StopAsync"/>, so the host lifecycle owns the subscription
/// instead of a top-level lambda captured in <c>Program.cs</c>.
/// </summary>
public sealed class MetabolismEventSubscriber : IHostedService
{
    private readonly MyceliumClient _myceliumClient;
    private readonly Metabolism _metabolism;
    private Action<Guid, string, object?>? _handler;

    public MetabolismEventSubscriber(MyceliumClient myceliumClient, Metabolism metabolism)
    {
        _myceliumClient = myceliumClient;
        _metabolism = metabolism;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = (relId, propName, value) =>
            _metabolism.UpdateProperty(relId.ToString(), propName, value);
        _myceliumClient.OnRelationshipPropertyChanged += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler != null)
        {
            _myceliumClient.OnRelationshipPropertyChanged -= _handler;
            _handler = null;
        }
        return Task.CompletedTask;
    }
}
