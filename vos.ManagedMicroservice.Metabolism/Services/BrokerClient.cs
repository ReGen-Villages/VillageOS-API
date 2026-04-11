using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using vos.Microservice.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>
/// HTTP client for communicating with the Ducati Broker (Metabolism-specific operations).
/// Adds SignalR subscription and quantity endpoint support on top of shared base.
/// </summary>
public class BrokerClient : BrokerClientBase
{
    private readonly string _mode;
    private HubConnection? _hubConnection;

    /// <summary>Raised when a relationship property changes on the broker.</summary>
    public event Action<Guid, string, object?>? OnRelationshipPropertyChanged;

    public BrokerClient(IHttpClientFactory httpClientFactory, ILogger<BrokerClient> logger, string brokerUrl, string mode, string? serviceToken = null)
        : base(httpClientFactory, logger, brokerUrl, serviceToken)
    {
        _mode = mode;
    }

    /// <summary>Registers this handler with the broker.</summary>
    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, $"Metabolism-{_mode}",
            $"dotnet run --project vos.ManagedMicroservice.Metabolism -- --port={port} --brokerUrl={BrokerUrl} --mode={_mode}");

    /// <summary>Deregisters this service from the broker and disconnects SignalR.</summary>
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
    /// Connects to the broker's SignalR hub to subscribe to property change events.
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
                    await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], ct);
                    continue;
                }

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{BrokerUrl}/vosHub", options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _hubConnection.On<Guid, string, object?>("RelationshipPropertyChanged",
                    (relationshipId, propertyName, newValue) =>
                    {
                        Logger.LogDebug("SignalR: RelationshipPropertyChanged {RelId} {Prop}={Value}",
                            relationshipId, propertyName, newValue);
                        OnRelationshipPropertyChanged?.Invoke(relationshipId, propertyName, newValue);
                    });

                _hubConnection.Reconnected += connectionId =>
                {
                    Logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                    return Task.CompletedTask;
                };

                await _hubConnection.StartAsync(ct);
                Logger.LogInformation("SignalR connected to broker hub");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SignalR connection attempt {Attempt} failed: {Error}", attempt + 1, ex.Message);
                await Task.Delay(delays[Math.Min(attempt, delays.Length - 1)], ct);
            }
        }
    }

    /// <summary>Applies the resource operation (increment or decrement) based on mode.</summary>
    public async Task<JsonElement?> ApplyQuantityAsync(string thingId, string propertyPath, decimal amount, string? subjectName = null, string? unit = null)
    {
        var action = _mode == "consumes" ? "decrements" : "increments";
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));

        var payload = new
        {
            amount,
            subjectName = subjectName ?? "",
            unit = unit ?? ""
        };

        var response = await client.PostAsJsonAsync(
            $"{BrokerUrl}/api/things/{thingId}/properties/{propertyPath}/{action}",
            payload
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

    /// <summary>Increment a property on a relationship (for tracking per-relationship cumulative totals).</summary>
    public async Task IncrementRelationshipPropertyAsync(string relationshipId, string propertyPath, decimal amount)
    {
        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));

        var payload = new { amount };

        var response = await client.PostAsJsonAsync(
            $"{BrokerUrl}/api/relationships/{relationshipId}/properties/{propertyPath}/increments",
            payload
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Relationship increment failed ({response.StatusCode}): {error}");
        }
    }
}
