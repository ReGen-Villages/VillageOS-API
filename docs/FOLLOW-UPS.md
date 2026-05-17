# Follow-ups

Deferred work items surfaced during in-flight phases. Each entry has enough context to pick the work back up cold. Convert to AzDO Bug/Task as the work is scheduled.

---

## 1. `vos.CLI/BrokerClient.cs` testability refactor

**Surfaced by:** Phase 2A (Task #5402, PR #382). 2A landed at 83.6% line coverage on `vos.CLI` with `BrokerClient` stuck at 3.2% — the 95% target is unreachable until this is resolved.

**Problem.** `BrokerClient` is 611 LOC (~19% of vos.CLI's source) and exposes 50+ HTTP methods (`GetAllThingsAsync`, `CreateThingAsync`, `SetPropertyAsync`, etc.). The constructor builds its own `HttpClientHandler` + `HttpClient` internally and assigns to private fields with no injection point:

```csharp
public BrokerClient(string brokerUrl, string? apiKey)
{
    _brokerUrl = brokerUrl.TrimEnd('/');
    _apiKey = apiKey ?? Environment.GetEnvironmentVariable("VOS_API_KEY");
    var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ... };
    _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
}
```

The existing CLI tests use `Mock<BrokerClient>(brokerUrl) { CallBase = false }` to **override** every method (every BrokerClient public method is `virtual`). That works for the command handlers but means BrokerClient's actual HTTP code is never exercised.

**Options for the refactor (one needs to be picked):**

1. **Add an internal HttpClient-injection constructor + `InternalsVisibleTo`.** Smallest production change. Add:
   ```csharp
   internal BrokerClient(string brokerUrl, string? apiKey, HttpClient httpClient) { ... }
   ```
   And `<InternalsVisibleTo Include="vos.CLI.Tests" />` in `vos.CLI.csproj`. Existing public ctor stays for production. Tests then use `MockHttpMessageHandler` from `vos.Tests.Shared` to fake broker responses.

2. **Make the HttpClient-injection constructor public.** Cleaner long-term API but a public-surface change. Anyone constructing `BrokerClient` externally needs to be aware of the new ctor. We own all callers in this repo, so the blast radius is contained, but it's a more invasive change.

3. **Test only what's reachable today** (constructor field assignment, `BuildTimeRangeQuery` static helper). Doesn't move the needle materially — vos.CLI aggregate would stay around 83-85% with BrokerClient still under 10%.

**Recommendation:** Option 1. Standard pattern for HttpClient testability in .NET; minimal blast radius; doesn't change the public API.

**Acceptance criteria once unblocked:**
- New `BrokerClientTests.cs` in `vos.CLI.Tests/`, exercising representative methods across each shape (GET-returns-JsonElement, POST-with-body-returns-JsonElement, DELETE-returns-bool, auth-flow-with-cached-token, auth-flow-token-expiry).
- `BrokerClient` line coverage ≥95%.
- `vos.CLI` aggregate ≥95%.
- Task #5402 closes.

---

## 2. `vos.CLI/SeedCommandHandler.cs` raw-string seed entries throw

**Surfaced by:** Phase 2A (Task #5402, PR #382). Documented in the test file but not fixed.

**Problem.** `ListLibrarySeedsAsync` formats each seed entry like this:

```csharp
foreach (var seed in seeds.EnumerateArray())
{
    var name = seed.GetStringOrDefault("Name", seed.ToString());
    _writer.WriteLine($"  {name}");
}
```

`GetStringOrDefault` calls `JsonElement.TryGetProperty("Name", ...)` which **throws** when invoked on a non-object element (e.g. a raw JSON string). The outer `try`/`catch` in `ExecuteAsync` swallows the throw and emits `"Error: The requested operation cannot be performed on a JSON string element"`, so the user sees an unhelpful error message instead of the seed list.

In practice the broker today returns each seed as an object with `Name` so this path doesn't fire — but the production code's apparent fallback is dead and the failure mode is hostile.

**Reproduce:** any broker response that returns `["seed-a", "seed-b"]` (raw strings) instead of `[{"Name":"seed-a"}, {"Name":"seed-b"}]` triggers it. Test:

```csharp
_brokerMock.Setup(b => b.ListLibrarySeedsAsync())
    .ReturnsAsync(JsonDocument.Parse("[\"raw-string-seed\"]").RootElement);
await ExecuteHandler("list");
// output contains "Error:" rather than "raw-string-seed"
```

**Fix sketch.** Guard against non-object seed entries:

```csharp
var name = seed.ValueKind == JsonValueKind.String
    ? seed.GetString() ?? "unknown"
    : seed.GetStringOrDefault("Name", seed.ToString());
```

Or push the guard into `JsonElementExtensions.GetStringOrDefault` so every caller benefits.

**Acceptance criteria:**
- Open as Bug #NNNN with the test reproduction above as Repro Steps.
- Fix in `SeedCommandHandler.cs` (or `JsonElementExtensions`); ship the regression test from `SeedCommandHandlerTests.cs` un-commented (currently a comment block in the test file).
- Existing happy-path tests still pass.

---

## How to use this file

1. When a phase surfaces a deferred item, add an entry here with enough context to pick it up cold.
2. When the deferred work is scheduled, convert the entry to an AzDO Bug/Task and remove from this file (leaving a one-line pointer with the work-item number for traceability).
3. This file complements `docs/TEST-STATE.md` (which tracks the *test landscape*) and `docs/TEST-COVERAGE-PLAN.md` (which tracks the *active execution plan*) — those describe the system; this describes work that needs to happen *to* the system.
