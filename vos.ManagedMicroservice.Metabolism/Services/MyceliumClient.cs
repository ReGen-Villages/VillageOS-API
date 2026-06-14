using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// HTTP client for communicating with the VOS Mycelium (Metabolism-specific operations).
/// Adds SignalR subscription and quantity endpoint support on top of shared base.
/// </summary>
public class MyceliumClient : MyceliumClientBase
{
    private readonly string _mode;
    private readonly IHubConnectionFactory _hubFactory;
    private IHubConnection? _hubConnection;

    /// <summary>Raised when a relationship property changes on Mycelium.</summary>
    public event Action<Guid, string, object?>? OnRelationshipPropertyChanged;

    public MyceliumClient(IHttpClientFactory httpClientFactory, ILogger<MyceliumClient> logger, string myceliumUrl, string mode, string? serviceToken = null, IHubConnectionFactory? hubFactory = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _mode = mode;
        _hubFactory = hubFactory ?? new DefaultHubConnectionFactory();
    }

    /// <summary>Registers this handler with Mycelium.</summary>
    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, $"Metabolism-{_mode}",
            $"dotnet run --project vos.ManagedMicroservice.Metabolism -- --port={port} --myceliumUrl={MyceliumUrl} --mode={_mode}");

    /// <summary>Deregisters this service from Mycelium and disconnects SignalR.</summary>
    public override async Task DeregisterAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
            Logger.LogInformation("SignalR connection closed");
        }

        await base.DeregisterAsync();
    }

    /// <summary>
    /// Connects to Mycelium's SignalR hub to subscribe to property change events.
    /// Retries with backoff until connected or cancelled.
    /// </summary>
    public async Task ConnectSignalRAsync(CancellationToken ct = default)
    {
        var delays = new[] { 0, 1000, 2000, 5000, 10000 };
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                var token = await GetTokenAsync();
                if (token == null)
                {
                    Logger.LogWarning("SignalR: cannot get token, will retry");
                    await DelayAsync(delays[Math.Min(attempt, delays.Length - 1)], ct);
                    continue;
                }

                _hubConnection = _hubFactory.Create(
                    $"{MyceliumUrl}/vosHub",
                    () => Task.FromResult<string?>(token));

                _hubConnection.On<Guid, string, object?>("RelationshipPropertyChanged", HandleRelationshipPropertyChanged);

                _hubConnection.Reconnected += HandleReconnected;

                await _hubConnection.StartAsync(ct);
                Logger.LogInformation("SignalR connected to mycelium hub");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SignalR connection attempt {Attempt} failed: {Error}", attempt + 1, ex.Message);
                await DelayAsync(delays[Math.Min(attempt, delays.Length - 1)], ct);
            }
        }
    }

    /// <summary>
    /// Backoff delay between SignalR connect attempts. Extracted as a seam so tests can
    /// assert the retry cadence without sleeping for real; production delegates to
    /// <see cref="Task.Delay(int, CancellationToken)"/>.
    /// </summary>
    protected virtual Task DelayAsync(int milliseconds, CancellationToken ct) => Task.Delay(milliseconds, ct);

    private const string ApplyQuantitySchemaId = "https://villageos/contracts/apply-quantity-request.schema.json";
    private const string RelationshipIncrementSchemaId = "https://villageos/contracts/relationship-property-increment-request.schema.json";
    private const string RelationshipPropertyChangedEventSchemaId = "https://villageos/contracts/relationship-property-changed-event.schema.json";

    /// <summary>
    /// Validates an inbound RelationshipPropertyChanged event payload against its schema
    /// then raises the public <see cref="OnRelationshipPropertyChanged"/> event. Separated
    /// from the SignalR callback so the validation path is unit-testable without a real hub.
    /// </summary>
    internal void RaiseRelationshipPropertyChanged(Guid relationshipId, string propertyName, object? newValue)
    {
        // Schema pins the JSON Hub Protocol arguments array shape: [uuid, string, untyped].
        var argsJson = JsonSerializer.Serialize(new object?[] { relationshipId, propertyName, newValue });
        ValidateOutbound(argsJson, RelationshipPropertyChangedEventSchemaId);
        OnRelationshipPropertyChanged?.Invoke(relationshipId, propertyName, newValue);
    }

    // Payload shape lives in a virtual builder so tests can inject a malformed object to
    // exercise the validation paths. Default returns the production wire shape.
    protected virtual object BuildApplyQuantityPayload(decimal amount, string? subjectName, string? unit) =>
        new { amount, subjectName = subjectName ?? "", unit = unit ?? "" };

    protected virtual object BuildIncrementRelationshipPayload(decimal amount) =>
        new { amount };

    /// <summary>Applies the resource operation (increment or decrement) based on mode.</summary>
    public async Task<JsonElement?> ApplyQuantityAsync(string thingId, string propertyPath, decimal amount, string? subjectName = null, string? unit = null)
    {
        var action = _mode == "consumes" ? "decrements" : "increments";

        var payload = BuildApplyQuantityPayload(amount, subjectName, unit);
        var json = JsonSerializer.Serialize(payload);

        // Validate the outbound payload before any network call. Throw mode propagates
        // ContractValidationException; Log mode warns and lets the call through.
        ValidateOutbound(json, ApplyQuantitySchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/things/{thingId}/properties/{propertyPath}/{action}",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new KeyNotFoundException($"Thing or property not found: {error}");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"POST {action} failed ({response.StatusCode}): {error}");
        }
    }

    private void HandleRelationshipPropertyChanged(Guid relationshipId, string propertyName, object? newValue)
    {
        Logger.LogDebug("SignalR: RelationshipPropertyChanged {RelId} {Prop}={Value}",
            relationshipId, propertyName, newValue);
        RaiseRelationshipPropertyChanged(relationshipId, propertyName, newValue);
    }

    private Task HandleReconnected(string? connectionId)
    {
        Logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    /// <summary>Increment a property on a relationship (for tracking per-relationship cumulative totals).</summary>
    public async Task IncrementRelationshipPropertyAsync(string relationshipId, string propertyPath, decimal amount)
    {
        var payload = BuildIncrementRelationshipPayload(amount);
        var json = JsonSerializer.Serialize(payload);

        ValidateOutbound(json, RelationshipIncrementSchemaId);

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        var response = await client.PostAsync(
            $"{MyceliumUrl}/api/relationships/{relationshipId}/properties/{propertyPath}/increments",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Relationship increment failed ({response.StatusCode}): {error}");
        }
    }
}
