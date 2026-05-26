# DI opportunities

Forward-looking notes on places where the codebase would benefit from dependency injection. Each item names a concrete current pain point and sketches the change that addresses it. Listed roughly in order of ROI (smallest scope × largest concrete benefit first).

| # | Area | Scope | Concrete payoff |
|---|---|---|---|
| 1 | Metabolism `Program.cs` — align with Tributary/Delta DI shape | Small | Consistency across microservices; cleaner test seams |
| 2 | `IHubConnectionBuilder` factory in Metabolism's BrokerClient | Small/Medium | Closes the largest single line-coverage gap (68.8 % → ~95 %+) |
| 3 | `IEndpointSeedProvider` for Delta — replaces static `EndpointSeedLoader.LoadDefault` | Small | Lifts the `DeltaFactoryCollection` serialization workaround; parallel tests restored |
| 4 | `vos.CLI` gets a `HostBuilder` | Large | **Proposed for discussion — not to be implemented yet** |

## 1. Metabolism `Program.cs` — align with Tributary/Delta DI shape

**Today.** Metabolism resolves its dependencies from the container by hand, then `new`s the application objects and captures them in the endpoint-mapping closure:

```csharp
var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
var logger = app.Services.GetRequiredService<ILogger<BrokerClient>>();
var brokerClient = new BrokerClient(httpClientFactory, logger, brokerUrl, mode, serviceToken);

var metabolism = new Metabolism(brokerClient, metabolismLogger, mode);
var processor = new HandleRequestProcessor(metabolism, processorLogger);

brokerClient.OnRelationshipPropertyChanged += (relId, propName, value) =>
    metabolism.UpdateProperty(relId.ToString(), propName, value);

app.MapMetabolismEndpoints(
    processor, metabolism, brokerClient, mode,
    () => requestCount, () => requestCount++,
    authEnabled: !string.IsNullOrEmpty(signingKey));
```

Tributary already does this the idiomatic way — registers `BrokerClient` as a singleton, lets endpoint handlers receive it through the parameter list.

**Proposed.**

```csharp
builder.Services.AddSingleton(sp =>
    new BrokerClient(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<BrokerClient>>(),
        brokerUrl, mode, serviceToken));
builder.Services.AddSingleton<Metabolism>();
builder.Services.AddSingleton<HandleRequestProcessor>();
builder.Services.AddHostedService<MetabolismEventSubscriber>(); // wires OnRelationshipPropertyChanged
```

Endpoint handlers then take the dependencies as parameters:

```csharp
app.MapPost("/handle", (HandleRequest req, HandleRequestProcessor processor) => processor.Handle(req));
```

**Benefits.**

- Three microservices, one shape. New microservices follow the same pattern without referring to "what does Metabolism do differently".
- `MetabolismWebApplicationFactory.ConfigureTestServices` can swap a fake `Metabolism` or `BrokerClient` cleanly via `services.RemoveAll<T>() + services.AddSingleton<T>(fake)` — same seam the other two microservices already use.
- The `OnRelationshipPropertyChanged` subscription becomes a real lifecycle-managed object rather than a lambda captured at startup. The hosted service controls its own `Start`/`StopAsync`.

**Effort.** Small. One Program.cs file, ~50 lines diff. No production behavior change.

## 2. `IHubConnectionBuilder` factory in Metabolism's BrokerClient

**Today.** `BrokerClient.ConnectSignalRAsync` constructs the SignalR connection inline:

```csharp
_hubConnection = new HubConnectionBuilder()
    .WithUrl($"{BrokerUrl}/vosHub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
    })
    .WithAutomaticReconnect()
    .Build();

_hubConnection.On<Guid, string, object?>("RelationshipPropertyChanged", HandleRelationshipPropertyChanged);
_hubConnection.Reconnected += HandleReconnected;
await _hubConnection.StartAsync(ct);
```

The retry/backoff/cancellation shell around this call is testable in principle, but no test can substitute the real hub. `vos.ManagedMicroservice.Metabolism.Services.BrokerClient` sits at **68.8 %** line coverage — the lowest in the codebase. The `HandleRelationshipPropertyChanged` and `HandleReconnected` handlers are unreachable from a unit test.

**Proposed.** Inject a factory for the hub connection:

```csharp
public interface IHubConnectionFactory
{
    IHubConnection Create(string url, Func<Task<string?>> accessTokenProvider);
}

public sealed class DefaultHubConnectionFactory : IHubConnectionFactory
{
    public IHubConnection Create(string url, Func<Task<string?>> accessTokenProvider) =>
        new HubConnectionBuilder()
            .WithUrl(url, opts => opts.AccessTokenProvider = accessTokenProvider)
            .WithAutomaticReconnect()
            .Build();
}
```

Register the default in Program.cs; the test factory substitutes a fake `IHubConnection` that records `On<T...>` registrations and exposes a method to fire events synchronously.

**Benefits.**

- Closes the bulk of the 30 % coverage gap with **real contract tests**, not test-padding:
  - cancellation token honored mid-retry
  - retry-after-failure backoff sequence matches the `delays[]` array
  - token re-fetched on each attempt
  - `RelationshipPropertyChanged` payload reaches `RaiseRelationshipPropertyChanged` with the right shape
- `HandleRelationshipPropertyChanged` and `HandleReconnected` become end-to-end testable rather than just covered through the internal-method seam.

**Effort.** Small/Medium. Most of the work is in the test-side fake (~80 lines). Production change is ~20 lines.

**Why this is the highest-ROI item.** Other coverage gaps are mostly defensive arms in pure helpers (`CriteriaLexer` error paths, `JSONata` unreachable branches). The Metabolism SignalR shell has real retry/cancellation logic that *would* surface real bugs if exercised — it's untested only because the dependency is non-injectable.

## 3. `IEndpointSeedProvider` for Delta

**Today.** Delta loads its endpoint template via a static call at startup:

```csharp
var endpointSeed = EndpointSeedLoader.LoadDefault(...);
```

`LoadDefault` walks three candidate paths under `AppContext.BaseDirectory`. In tests, `DeltaWebApplicationFactory.InitializeAsync` writes a synthetic `seed.json` to that same directory — but it's a process-global path, so two factories running concurrently clobber each other's seed file (and the file is deleted in `DisposeAsync`, so one test's teardown can break another test's setup).

A `DeltaFactoryCollection` xunit collection serializes Delta-factory tests as a workaround. It works, but documents itself as a workaround.

**Proposed.**

```csharp
public interface IEndpointSeedProvider
{
    RegisterEndpointRequest LoadSeed();
}

public sealed class FileEndpointSeedProvider : IEndpointSeedProvider
{
    private readonly ILogger _log;
    public FileEndpointSeedProvider(ILogger<FileEndpointSeedProvider> log) { _log = log; }
    public RegisterEndpointRequest LoadSeed() => EndpointSeedLoader.LoadDefault(_log);
}
```

Program.cs:

```csharp
builder.Services.AddSingleton<IEndpointSeedProvider, FileEndpointSeedProvider>();
// ...
var endpointSeed = app.Services.GetRequiredService<IEndpointSeedProvider>().LoadSeed();
```

The test factory provides an in-memory seed:

```csharp
public string SeedJson { get; set; } = """{ "name": "Endpoint", "properties": { ... } }""";

protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IEndpointSeedProvider>();
        services.AddSingleton<IEndpointSeedProvider>(new InMemoryEndpointSeedProvider(SeedJson));
    });
    // no more File.WriteAllText / File.Delete dance in Initialize/DisposeAsync
}
```

**Benefits.**

- Each `DeltaWebApplicationFactory` instance has its own seed. No process-global filesystem state.
- `DeltaFactoryCollection` is no longer needed — delete it. Delta tests parallelize.
- Malformed-seed boot tests (`EndpointSeedBootTests.LoadEndpointSeed_MalformedJson_ThrowsAtStartup`) still work: set `SeedJson = "{ invalid"` and the in-memory provider throws at host startup, same contract as the file path version.
- `EndpointSeedLoader.LoadDefault` and the `Load(paths, log)` overload stay as-is — production path unchanged.

**Effort.** Small. New interface, one production provider, one test provider, ~5 lines in Program.cs, ~5 lines in `DeltaWebApplicationFactory`. Deletes `DeltaFactoryCollection.cs` and the `[Collection(...)]` attributes on the factory-using test classes.

## 4. `vos.CLI` gets a `HostBuilder`

> **Status: proposed for discussion — not to be implemented yet.** This is a significant refactor. Captured here so the option is documented and rationale is preserved; implementation should be its own Feature with its own Tasks, scoped after items 1–3 have landed.

**Today.** `vos.CLI` uses a plain `static void Main` entry point with manual command-handler construction. Several rough edges fall out of that:

- `vos.CLI.BrokerClient` is unit-testable only via an `internal` HttpClient-injection constructor plus `InternalsVisibleTo`. `docs/TEST-STATE.md` flags this as a workaround.
- `Program` shows 0 % coverage in the snapshot. The entry-point exclusion is honest — there's no shell to test, just a `Main` that wires things up — but a `HostBuilder` makes `Program` a thin bootstrap that *can* be touched by integration tests if the bootstrap logic ever becomes non-trivial.
- Configuration is parsed by hand. `ConsoleOptions.Parse` reads `VOS_BROKER_URL` / `VOS_API_KEY` env vars directly via `Environment.GetEnvironmentVariable`.
- Logging happens through `Console.WriteLine` rather than `ILogger`, so output is hard to capture or route in tests.
- `vos.CLI.Tests/CliEnvVarCollection.cs` exists exclusively to serialize tests that mutate those env vars — the same family of problem that microservice `EnvVarScope` helpers solved, but rooted in the CLI's own production interface, which is why it survives.

**Sketch.**

```csharp
public static async Task<int> Main(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IBrokerClient, BrokerClient>();
    builder.Services.AddSingleton<CommandHandler>();
    builder.Services.AddSingleton<CommandParser>();
    // ... etc per command handler

    var host = builder.Build();
    var repl = ActivatorUtilities.CreateInstance<Repl>(host.Services);
    return await repl.RunAsync();
}
```

`Repl` becomes a real testable type that takes its dependencies via constructor; the REPL loop runs on the main thread after `host.Start()` (host services live for the duration of the REPL).

**Benefits.**

- Drops the `InternalsVisibleTo` + internal-ctor hack on `BrokerClient`.
- `IConfiguration` picks up env vars automatically — `VOS_BROKER_URL` / `VOS_API_KEY` continue to work without `ConsoleOptions` parsing them by hand. Tests inject overrides via `host.Services.GetRequiredService<IConfiguration>` substitution, no process-environment mutation.
- `CliEnvVarCollection` can be deleted. CLI tests parallelize.
- `ILogger<T>` replaces `Console.WriteLine` everywhere output is structured (errors, diagnostics). Direct `Console.WriteLine` stays only where the REPL is writing user-visible prompts.

**Costs and open questions.**

- The REPL is interactive — `Host.Run()` blocks, so the REPL has to be either a hosted service or run on the main thread after `host.Start()` with `host.StopAsync()` on exit. Both work; need to pick one.
- ~540 existing CLI tests construct handlers directly with `new`. Migrating each to DI is mechanical but touches many files.
- Public API decision: should `BrokerClient` get an `IBrokerClient` interface? Today tests Moq the concrete class.
- `ConsoleOptions` has its own test suite. Decide whether to keep it as a thin façade over `IConfiguration` or replace it entirely.

**When to do this.** After items 1–3 land. Don't bundle — this Feature stands alone and reviewers should evaluate it on its own merits.

## What's deliberately not on this list

The codebase has several static helpers (`JsonValueCoercion`, `JsonValueUnwrapper`, `EffectivePropertyResolver`, `EndpointSeedLoader.Load`) that look DI-shaped but shouldn't be. They're pure functions with no dependencies and no lifetime, already 100 % covered by direct unit tests; turning them into services would add ceremony with no testing benefit.

`Serilog.Log.Logger` is intentionally a static global for bootstrap logging (before the host is built and `ILogger<T>` is available). It coexists with `Microsoft.Extensions.Logging` once the host is up. No change needed.

`CliArgs` is a static `Parse` — input goes in, output comes out, no dependencies. Routed through `IConfiguration` for the fallback (post Task #5453), which is the right shape; the `Parse` method itself is correctly a pure static.
