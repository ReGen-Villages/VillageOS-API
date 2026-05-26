using System.Collections.Concurrent;
using vos.ManagedMicroservice.Metabolism.Helpers;
using vos.ManagedMicroservice.Metabolism.Models;

namespace vos.ManagedMicroservice.Metabolism.Services;

/// <summary>Manages continuous simulation loops for registered resource relationships.</summary>
public class Metabolism
{
    private readonly ConcurrentDictionary<string, SimulationEntry> _simulations = new();
    private readonly BrokerClient _brokerClient;
    private readonly ILogger<Metabolism> _logger;
    private readonly string _mode;
    private readonly object _updateLock = new();
    private int _registrationOrder;

    public Metabolism(BrokerClient brokerClient, ILogger<Metabolism> logger, string mode)
    {
        _brokerClient = brokerClient;
        _logger = logger;
        _mode = mode;
    }

    public SimulationEntry Register(SimulationConfig config)
    {
        // Cancel existing simulation for same relationship if re-registered
        if (_simulations.TryRemove(config.RelationshipId, out var existing))
        {
            existing.Cts.Cancel();
            _logger.LogInformation("Cancelled previous simulation for {RelId}", config.RelationshipId);
        }

        var cts = new CancellationTokenSource();
        var order = Interlocked.Increment(ref _registrationOrder);
        var entry = new SimulationEntry
        {
            Config = config,
            Cts = cts,
            RegisteredAt = DateTime.UtcNow,
            Status = "waiting"
        };

        entry.RunningTask = RunSimulationLoop(entry, order, cts.Token);
        _simulations[config.RelationshipId] = entry;
        return entry;
    }

    private async Task RunSimulationLoop(SimulationEntry entry, int order, CancellationToken ct)
    {
        var config = entry.Config;
        var verb = _mode == "consumes" ? "decrementing" : "incrementing";

        // Phase 1: Wait for startDelaySeconds before considering startUtc
        if (config.StartDelaySeconds > 0)
        {
            var delayMs = (int)(config.StartDelaySeconds * 1000m);
            _logger.LogInformation("Simulation {RelId}: delaying {Seconds}s before start",
                config.RelationshipId, config.StartDelaySeconds);
            entry.Status = "delayed";
            try { await Task.Delay(delayMs, ct); }
            catch (OperationCanceledException) { entry.Status = "cancelled"; return; }
        }

        // Phase 2: Wait until startUtc if in the future
        var waitTime = config.StartUtc - DateTime.UtcNow;
        if (waitTime > TimeSpan.Zero)
        {
            entry.Status = "waiting";
            _logger.LogInformation("Simulation {RelId}: waiting {Seconds}s until start time",
                config.RelationshipId, waitTime.TotalSeconds);
            try { await Task.Delay(waitTime, ct); }
            catch (OperationCanceledException) { entry.Status = "cancelled"; return; }
        }
        else
        {
            // Stagger initial ticks to avoid thundering herd on startup.
            // Each simulation waits (order * 200ms) + random jitter before first tick.
            var staggerMs = (order * 200) + Random.Shared.Next(0, 500);
            _logger.LogDebug("Simulation {RelId}: staggering initial tick by {Ms}ms", config.RelationshipId, staggerMs);
            try { await Task.Delay(staggerMs, ct); }
            catch (OperationCanceledException) { entry.Status = "cancelled"; return; }
        }

        entry.Status = "active";
        _logger.LogInformation("Simulation {RelId}: active — {Verb} {Qty} {Unit} every {Freq}s on {Target}",
            config.RelationshipId, verb, config.Quantity, config.Unit, config.FrequencySeconds, config.TargetId);

        while (!ct.IsCancellationRequested && DateTime.UtcNow < config.EndUtc)
        {
            try
            {
                await _brokerClient.ApplyQuantityAsync(config.TargetId, config.PropertyPath, config.Quantity, config.SubjectName, config.Unit);
                entry.TickCount++;
                entry.LastTickUtc = DateTime.UtcNow;
                entry.LastError = null;

                // Track per-relationship cumulative total (best-effort — pool operation already succeeded)
                try
                {
                    var trackingProp = _mode == "consumes" ? "total_consumed" : "total_produced";
                    await _brokerClient.IncrementRelationshipPropertyAsync(
                        config.RelationshipId, trackingProp, config.Quantity);
                }
                catch (Exception relEx)
                {
                    _logger.LogWarning("Simulation {RelId}: relationship tracking failed: {Error}",
                        config.RelationshipId, relEx.Message);
                }
            }
            catch (Exception ex)
            {
                entry.LastError = ex.Message;
                _logger.LogWarning("Simulation {RelId} tick failed: {Error}", config.RelationshipId, ex.Message);
            }

            var sleepMs = config.FrequencySeconds * 1000;
            try { await Task.Delay(sleepMs, ct); }
            catch (OperationCanceledException) { entry.Status = "cancelled"; return; }
        }

        entry.Status = "completed";
        _logger.LogInformation("Simulation {RelId}: completed after {Ticks} ticks", config.RelationshipId, entry.TickCount);
    }

    /// <summary>
    /// Update a single property on a running simulation, restarting the loop with the new config.
    /// </summary>
    public void UpdateProperty(string relationshipId, string propertyName, object? newValue)
    {
        // Lock so rapid sequential changes (e.g. quantity then frequencySeconds) don't race —
        // without this, both threads read the same old config and the first change is lost.
        lock (_updateLock)
        {
            if (!_simulations.TryGetValue(relationshipId, out var entry))
                return;

            // SignalR delivers values as JsonElement — unwrap to native types
            var value = JsonValueUnwrapper.Unwrap(newValue);

            var old = entry.Config;
            SimulationConfig? updated = null;
            try
            {
                updated = propertyName switch
                {
                    "quantity" => old with { Quantity = Convert.ToDecimal(value) },
                    "frequencySeconds" => old with { FrequencySeconds = Convert.ToInt32(value) },
                    "unit" => old with { Unit = value?.ToString() ?? "" },
                    "propertyPath" => old with { PropertyPath = value?.ToString() ?? "quantity" },
                    "startDelaySeconds" => old with { StartDelaySeconds = Convert.ToDecimal(value) },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to convert property {Prop}={Value}: {Error}", propertyName, value, ex.Message);
                return;
            }

            if (updated == null) return;

            _logger.LogInformation("Simulation {RelId}: property {Prop} changed to {Value} — restarting",
                relationshipId, propertyName, value);
            Register(updated);
        }
    }

    public bool Cancel(string relationshipId)
    {
        if (_simulations.TryRemove(relationshipId, out var entry))
        {
            entry.Cts.Cancel();
            return true;
        }
        return false;
    }

    public virtual IEnumerable<SimulationEntry> GetAll() => _simulations.Values;

    public async Task StopAllAsync()
    {
        foreach (var entry in _simulations.Values)
            entry.Cts.Cancel();

        var tasks = _simulations.Values.Select(e => e.RunningTask).ToArray();
        if (tasks.Length > 0)
            await Task.WhenAll(tasks);

        _simulations.Clear();
    }
}

public class SimulationEntry
{
    public required SimulationConfig Config { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public Task RunningTask { get; set; } = Task.CompletedTask;
    public DateTime RegisteredAt { get; init; }
    public int TickCount { get; set; }
    public DateTime? LastTickUtc { get; set; }
    public string Status { get; set; } = "waiting";
    public string? LastError { get; set; }
}
