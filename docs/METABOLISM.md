# Metabolism Microservice

## What It Does

The Metabolism service is a persistent daemon that simulates continuous resource flows in a VillageOS graph. It serves two predicates — `consumes` and `produces` — from a single binary, differentiated by a `--mode` CLI argument.

When a relationship like `Chemistry-Test --[consumes]--> Reagent-Pool` is created, the broker notifies the Metabolism service. The handler then runs a continuous loop: every N seconds, it decrements (or increments, for `produces`) a numeric property on the target thing. This turns VillageOS's static graph into a live simulation where resource quantities change over time.

**In concrete terms**: if a village has 5 homes that each `consumes` electricity from a shared pool, the Metabolism service runs 5 independent loops, each decrementing the pool's `quantity` property at its own rate. The pool's value drops in real-time, and any ranges defined on it (e.g., "Low Power Alert" when `quantity < 50`) evaluate automatically.

## Why This Architecture

### One binary, two predicates

`consumes` and `produces` are mirror images — one decrements, the other increments. Rather than maintain two nearly-identical projects, a single `Metabolism` binary accepts `--mode=consumes` or `--mode=produces`. The predicate thing's `ServiceArgs` property tells the broker which mode to pass:

```json
{ "ServiceArgs": "--mode=consumes" }
```

This means there are two running processes (on ports 7102 and 7103), but built from the same source.

### Daemon mode, not request-response

The handler doesn't do one thing and exit. It stays alive, running simulation loops for every relationship registered with it. This avoids process startup overhead (dotnet cold start is expensive) and lets it maintain in-memory state about all active simulations.

### SignalR for live updates

When someone changes a relationship property in the GUI (say, increasing `frequencySeconds` from 30 to 60), the handler hears about it via SignalR in real-time. It cancels the running simulation loop and restarts it with the new config. No broker round-trip, no re-invocation needed.

### Staggered ticks

When a seed loads with 20 `consumes` relationships, all 20 get registered within milliseconds. If they all fired their first tick simultaneously, the broker would get hammered with 20 concurrent API calls. The handler staggers initial ticks: each simulation waits `(registration_order * 200ms) + random_jitter` before its first tick. After that, each runs on its own independent timer.

## How It Works

### Startup sequence

```
1. Parse CLI args (--port, --brokerUrl, --mode, --token, --signingKey)
2. Use pre-minted service JWT received via --token for broker authentication
3. Start ASP.NET minimal API on the given port (with broker token validation via vos.Auth.Shared)
4. Connect to broker's SignalR hub for property change events
5. Wait for /handle requests from the broker (validated via broker-signed request tokens)
```

The handler does **not** self-register with the broker on startup in the normal flow — the broker discovers it by successfully calling `/handle` or `/health`. Registration happens as a courtesy so the broker can track the handler for graceful shutdown.

### The `/handle` request

When a relationship using the `consumes` or `produces` predicate is created (or re-loaded from a seed), the broker POSTs to `/handle`:

```json
{
  "relationshipId": "abc-123",
  "subjectId": "chemistry-test-guid",
  "targetId": "reagent-pool-guid",
  "subjectName": "Chemistry-Test-Run-1",
  "targetName": "Reagent-Lot-A",
  "properties": {
    "quantity": 5.0,
    "unit": "mL",
    "frequencySeconds": 30,
    "propertyPath": "quantity"
  }
}
```

The `properties` field contains the relationship's own properties, sent inline so the handler doesn't need to call back to the broker to read them.

### Simulation lifecycle

Each `/handle` request creates one simulation loop in the `Metabolism` engine:

```
delayed → waiting → active → completed (or cancelled)
```

**delayed**: If `startDelaySeconds > 0`, the loop sleeps for that duration first. Used to stagger different stages of a process (e.g., reagent consumption starts 10 seconds after the process begins).

**waiting**: If `startUtc` is in the future, the loop sleeps until that time.

**active**: The main loop. On each tick:
1. Call broker's `POST /api/things/{targetId}/decrement-quantity` (or `increment-quantity`)
2. Increment `total_consumed` (or `total_produced`) on the relationship itself (best-effort)
3. Sleep for `frequencySeconds`

**completed**: The loop exits when `endUtc` is reached or the simulation is cancelled.

If a relationship is registered again (e.g., on seed reload), the previous simulation is cancelled and replaced.

### Live hot-reload

The handler subscribes to `RelationshipPropertyChanged` events on the broker's SignalR hub. When a tracked property changes:

| Property | Effect |
|----------|--------|
| `quantity` | Changes the amount applied per tick |
| `frequencySeconds` | Changes the tick interval |
| `unit` | Updates the unit label |
| `propertyPath` | Changes which property on the target is modified |
| `startDelaySeconds` | Changes the initial delay (restarts the loop) |

The `Metabolism.UpdateProperty` method uses a lock to prevent race conditions when multiple properties change in rapid succession (e.g., a user updates both `quantity` and `frequencySeconds` in the GUI). Each change cancels the current loop and starts a fresh one with the updated config.

## Code Structure

```
vos.ManagedMicroservice.Metabolism/
├── Program.cs                          # Entry point, wiring
├── Configuration/
│   └── CliArgs.cs                      # CLI argument parsing
├── Models/
│   ├── HandleRequest.cs                # /handle request payload
│   └── SimulationConfig.cs             # Simulation loop parameters
├── Services/
│   ├── BrokerClient.cs                 # HTTP + SignalR communication with broker
│   ├── Metabolism.cs                   # Simulation loop engine
│   └── HandleRequestProcessor.cs       # Request validation + config extraction
└── Endpoints/
    └── EndpointMapper.cs               # HTTP endpoint definitions
```

### Key classes

**`Metabolism`** — The simulation engine. Holds a `ConcurrentDictionary<string, SimulationEntry>` keyed by relationship ID. Each entry has its own async loop running in a `Task`. Handles registration, cancellation, property hot-reload, and graceful shutdown.

**`BrokerClient`** — All communication with the broker. Uses pre-minted service token from `--token` startup arg (with open-endpoint fallback), service registration/deregistration, quantity increment/decrement API calls, and SignalR hub connection with retry backoff.

**`HandleRequestProcessor`** — Pure extraction logic. Takes a `HandleRequest`, extracts a `SimulationConfig` with sensible defaults, and registers it with Metabolism. The `ExtractConfig` method is `internal static` and side-effect-free, making it testable.

**`SimulationEntry`** — Runtime state for one simulation: the config, a `CancellationTokenSource`, tick count, last tick time, status string, and last error message.

## HTTP Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/handle` | POST | Register a relationship for simulation |
| `/simulations` | GET | List all simulations with status and tick counts |
| `/simulations/{relationshipId}` | DELETE | Cancel a specific simulation |
| `/health` | GET | Health check (active/total simulation counts) |
| `/stats` | GET | Service metadata (handler ID, broker URL, version) |
| `/shutdown` | POST | Stop all simulations, deregister, exit |

## Relationship Properties

These are set on the relationship (not the things) and control the simulation:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `quantity` | number | 1.0 | Amount per tick |
| `propertyPath` | string | `"quantity"` | Which property on the target thing to modify |
| `unit` | string | `""` | Unit label (for logging and display) |
| `frequencySeconds` | number | 60 | Seconds between ticks |
| `startDelaySeconds` | number | 0 | Delay before simulation begins |
| `startUtc` | ISO 8601 | now | When to start ticking |
| `endUtc` | ISO 8601 | 2099-12-31 | When to stop |

> **Typed envelopes in seed files**: When defining metabolism properties in seed JSON files, numeric properties (`quantity`, `frequencySeconds`, `startDelaySeconds`, `reorder_point`) **must** use typed envelopes: `{"typeInfo": "vos.Decimal", "value": 5.0}`. Plain numeric values are stored as `vos.Integer`, which truncates decimal increments to 0. This applies to both pool thing properties and relationship properties. See [Broker Guide — Seed Format](BROKER_GUIDE.md) for details.

## How to Use

### 1. Define the predicate things in your seed

The broker needs predicate things with handler configuration:

```json
{
  "Name": "consumes",
  "Properties": {
    "ExecutablePath": "../vos.ManagedMicroservice.Metabolism/bin/Debug/net10.0/vos.ManagedMicroservice.Metabolism.dll",
    "ServicePort": 7102,
    "ServiceArgs": "--mode=consumes",
    "onLoad": true
  }
}
```

```json
{
  "Name": "produces",
  "Properties": {
    "ExecutablePath": "../vos.ManagedMicroservice.Metabolism/bin/Debug/net10.0/vos.ManagedMicroservice.Metabolism.dll",
    "ServicePort": 7103,
    "ServiceArgs": "--mode=produces",
    "onLoad": true
  }
}
```

`onLoad: true` is important — it tells the broker to re-invoke the handler for all existing relationships when a seed is loaded. Since simulation state is in-memory (not serialized), simulations must be re-started on every load.

### 2. Create a resource pool

```json
POST /api/things
{
  "Name": "Electricity-Pool",
  "Properties": { "quantity": 1000.0, "unit": "kWh" }
}
```

### 3. Create a consumer

```json
POST /api/relationships
{
  "SubjectId": "{home-id}",
  "PredicateId": "{consumes-predicate-id}",
  "TargetId": "{electricity-pool-id}",
  "Properties": {
    "quantity": 0.5,
    "unit": "kWh",
    "frequencySeconds": 10
  }
}
```

The broker will auto-start the Metabolism service (if not already running), call `/handle`, and the simulation begins. Every 10 seconds, the pool's `quantity` drops by 0.5.

### 4. Run manually (for development/debugging)

```bash
dotnet run --project vos.ManagedMicroservice.Metabolism -- \
  --port=7102 --brokerUrl=https://localhost:7243 --mode=consumes
```

Then check status:

```bash
curl http://localhost:7102/health
curl http://localhost:7102/simulations
```

### 5. Monitor and adjust live

Change a relationship property in the GUI or via API — the handler picks it up via SignalR and adjusts immediately. No restart needed.

## Shutdown

On `ApplicationStopping`:
1. All simulation loops are cancelled via their `CancellationTokenSource`
2. `Task.WhenAll` waits for all loops to finish
3. The handler deregisters from the broker via `DELETE /api/broker/services/{handlerId}`
4. The SignalR connection is disposed

The broker can also trigger shutdown by POSTing to `/shutdown`, which follows the same sequence.

## Logging

Logs go to `logs/metabolism-{mode}-YYYYMMDD.log` (rolling daily). File-only logging — no console sink. The broker does **not** redirect stdout (`RedirectStandardOutput = false`), so console output goes to the broker's own console or is lost. **Use file-based logging only (Serilog `WriteTo.File`) for reliable diagnostics.**
