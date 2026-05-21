# Test State

A living snapshot of how unit tests are organized, what's covered, and where the gaps are. Update this when the test landscape changes — do **not** put hardcoded test counts here (they rot on the next PR).

## Where tests live

.NET test projects (all `net10.0`, all listed in `VillageOS-API.sln`):

| Project | Path | Mocking | Production code covered |
|---|---|---|---|
| `vos.CLI.Tests` | `vos.CLI.Tests/` | Moq | `vos.CLI` only — the Application/Core/Infrastructure layers are mocked at the seam, so they get no transitive coverage from this project (verified against Cobertura output) |
| `vos.ManagedMicroservice.Metabolism.Tests` | `Tests/vos.ManagedMicroservice.Metabolism.Tests/` | Moq + `MockHttpMessageHandler` | Metabolism (consumes/produces simulation lifecycle and the broker client) |
| `vos.ManagedMicroservice.Tributary.Tests` | `Tests/vos.ManagedMicroservice.Tributary.Tests/` | NSubstitute + `MockHttpMessageHandler` + `WebApplicationFactory<Program>` | Tributary — `/handle`, `/health`, `/shutdown` endpoints, `BrokerClient`, `ObservationIngestService`, `CliArgs` (post Phase 2C) |
| `vos.ManagedMicroservice.Delta.Tests` | `Tests/vos.ManagedMicroservice.Delta.Tests/` | NSubstitute + `MockHttpMessageHandler` + `WebApplicationFactory<Program>` | Delta — `/handle`, `/register`, `/health`, `/shutdown` endpoints, `BrokerClient`, `CliArgs`, `LoadEndpointSeed`, compensation logic (post Phase 2D) |
| `vos.Auth.Shared.Tests` | `Tests/vos.Auth.Shared.Tests/` | None (no mocks needed — pure helpers + extension methods) | `vos.Auth.Shared` (`ServiceTokenValidator`, `HandlerAuthExtensions`, constants) |
| `vos.ManagedMicroservice.Shared.Tests` | `Tests/vos.ManagedMicroservice.Shared.Tests/` | `NullLogger` + `MockHttpMessageHandler` (no mocking-library dependency) | `vos.ManagedMicroservice.Shared` (`BrokerClientBase` via a thin `TestableBrokerClient` subclass, `HttpMethodValidator`, `RequiredPropertyValidator`) |
| `vos.Core.Tests` | `Tests/vos.Core.Tests/` | None (pure domain types — no I/O to mock) | All of `vos.Core`: Domain root (`VosModel`, `VosObject`, `VosProperty`, `VosThing`, `VosRelationship`, `TemporalGraphSnapshot` + `TemporalQuery` extensions, `InheritedPropertySet`, `PropertyMode`/`PropertyModeConfiguration`, exceptions, `IfcGeometry`/`LatLng`/`GeoJson`) plus `ExpectedValues/` (criteria DSL parser/lexer, `CriteriaExpr` hierarchy, `RangeBounds` hierarchy, `ExpectedRange`, `DependencyGraph`, `StateIndex`, `StateHistoryTracker`, `RangeEvaluationService` with cycle detection). Files split across `Domain/` and `ExpectedValues/` subfolders. |
| `vos.Application.Tests` | `Tests/vos.Application.Tests/` | Moq for `IMicroserviceHandler` (handler-invocation paths in `VosRelationshipService`); `NullLogger` elsewhere | `vos.Application` services (`VosObjectService`, `VosThingService`, `VosRelationshipService`, `VosModelService`), `SurfaceThingClassifier`, `JsonValueConverter`, and `SerializationHelpers` (internal — exercised through the Thing/Relationship services' `ToJsonFragment*` methods with full range/binding/inherited-set fixtures). |
| `vos.Infrastructure.Tests` | `Tests/vos.Infrastructure.Tests/` | None (pure infrastructure types with concrete dependencies) | `vos.Infrastructure` (`ModelStore` concurrency + thread-safety, `VosModelProvider` JSON deserialization including round-trip fixtures, all four `RangeBounds` types, nested inherited property sets, criteria-DSL parsing through `CriteriaParser`, missing-array branches, `ClearModel`). |
| `vos.ManagedMicroservice.Echo.Tests` | `Tests/vos.ManagedMicroservice.Echo.Tests/` | `NullLogger` + `MockHttpMessageHandler` (no mocking-library dependency) | `vos.ManagedMicroservice.Echo` `CliArgs` + `BrokerClient` (Program.cs is integration-test territory and excluded). Tests are written as the **canonical template** for any ManagedMicroservice — see `docs/MICROSERVICE-TEMPLATE.md` for the patterns other microservice test projects should mirror. |

All test projects use xUnit + FluentAssertions. The CLAUDE.md note about test projects living in two places by historical accident (`vos.CLI.Tests/` at the repo root, microservice tests under `Tests/`) still holds.

GUI tests (`vos.GUI/`):

- **Vitest unit suite** — broad coverage across APIs, components, hooks, stores, and utils. Config inline in `vos.GUI/vite.config.ts` (jsdom env, v8 coverage). Scripts: `npm test`, `npm run test:coverage`.
- **Puppeteer e2e** — a single model-viewer smoke test at `vos.GUI/test/e2e/model-viewer.e2e.mjs`, driven by `node --test`. Script: `npm run test:e2e`.

## CI signal

`azure-pipelines.yml`:

- `.NET` tests run via `dotnet test **/*Tests.csproj --configuration Release --no-build --logger trx --collect:"XPlat Code Coverage"` with `failTaskOnFailedTests: true`. **Failing .NET tests block the pipeline.**
- Coverage is published as a Cobertura artifact. **No threshold is enforced** — collection only.
- **GUI tests do not run in CI.** Neither `npm test` nor `npm run test:e2e` are wired into the pipeline. The Vitest suite and the Puppeteer smoke test currently provide no merge-gate signal.

## Coverage

Numbers below are from a local `dotnet test --collect:"XPlat Code Coverage"` run against the full solution (`coverage.runsettings`, Cobertura format). Refresh by re-running and re-reading the Cobertura XMLs under `TestResults/`. Don't commit specific numbers — these will rot; the table is a *current snapshot* for orientation, not a contract.

| Assembly | Line % | Branch % | Source of coverage |
|---|---|---|---|
| `vos.CLI` | ~96% | ~88% | `vos.CLI.Tests` (Phase 2A, Task #5402): 5 new handler test files (BrokerStatus / User / Model / Seed / State CommandHandler) all at 100%; CommandHandler dispatcher gaps closed; `BrokerClient` refactored with an internal HttpClient-injection ctor + `InternalsVisibleTo` and tested to 99.2% via `MockHttpMessageHandler`. |
| `vos.ManagedMicroservice.Metabolism` | ~93% pkg-level, every declared source file 95-100% (see WebApplicationFactory note below) | ~81% | `Tests/vos.ManagedMicroservice.Metabolism.Tests/` (Phase 2B, Task #5403) |
| `vos.ManagedMicroservice.Shared` | 100% | 100% | `Tests/vos.ManagedMicroservice.Shared.Tests/` (Phase 1F, Task #5401); previously ~31% as a side effect of the three microservice test runs |
| `vos.ManagedMicroservice.Tributary` | ~95% pkg-level; CliArgs / EndpointCallRequest / ObservationIngestResult 100%, BrokerClient 99.4%, Program 94.3%, ObservationIngestService 89.1% | ~92% | `Tests/vos.ManagedMicroservice.Tributary.Tests/` (Phase 2C, Task #5404). WebApplicationFactory&lt;Program&gt; pattern (see notes). Sub-95% files are Program.cs minimal-API wireup + ObservationIngestService defensive-only branches per the plan's documented exclusion language. |
| `vos.ManagedMicroservice.Delta` | ~94% pkg-level; CliArgs / RegisterEndpointRequest / BrokerClient 100%, Program 89.9% | ~90% | `Tests/vos.ManagedMicroservice.Delta.Tests/` (Phase 2D, Task #5405). WebApplicationFactory&lt;Program&gt; pattern. Program.cs &lt; 95% is the minimal-API wireup + the defensive outer-catch (`HandleRegisterEndpointRequestAsync` catch wraps the whole handler against runtime exceptions that the broker-client try/catches already swallow) — both excluded by the plan. |
| `vos.Core` | 95% | 87% | `Tests/vos.Core.Tests/` (Phase 1A; 1A.1 Task #5410 + 1A.2 Task #5411). Most classes 100%; CriteriaParser/Lexer at ~82% (DSL error paths defensive), RangeEvaluationService at ~94% (cycle-detection branches). |
| `vos.Application` | 98% | 88% | `Tests/vos.Application.Tests/` (Phase 1B, Task #5397). All 7 source files at 95%+. |
| `vos.Infrastructure` | 99% | 83% | `Tests/vos.Infrastructure.Tests/` (Phase 1C, Task #5398). `ModelStore` 100%; `VosModelProvider` 98.6%. |
| `vos.Auth.Shared` | 100% | n/a (no branches) | `Tests/vos.Auth.Shared.Tests/` (Phase 1D, Task #5399) |
| `vos.ManagedMicroservice.Echo` | 100% (CliArgs + BrokerClient) | 100% | `Tests/vos.ManagedMicroservice.Echo.Tests/` (Phase 1E, Task #5400). Program.cs is integration-test scope and excluded from this measurement. |
| `vos.GUI` | not measured here | — | `npm run test:coverage` (Vitest + v8); not included in this snapshot |

**Headline:** the three pure-library projects at the bottom of the .NET stack (`vos.Core`, `vos.Application`, `vos.Infrastructure`) currently have zero direct test coverage — the CLI tests mock everything beneath the handler boundary, so the domain model itself is not exercised. Among microservices, Metabolism is well-covered and Echo is unmeasured because it has no test project at all.

## Quality signals

Healthy:

- Zero skipped tests (no `Skip = `, `[Fact(Skip…`, `it.skip`, `xit`, etc.).
- Zero `TODO`/`FIXME`/`HACK` markers inside test files.
- No flakiness markers (`// FIXME flaky`, `// TODO re-enable`).
- Test projects are actively maintained, not stale.
- The recently imported Metabolism test suite is wired into the solution and discovered by the `**/*Tests.csproj` glob.
- Full solution test run is green locally.

Watch items:

1. **The domain layer (`vos.Core` + `vos.Application` + `vos.Infrastructure`) has 0% direct coverage.** CLI tests mock the seam below the handlers; nothing exercises the actual model, services, or storage. This is the largest single gap. (Pre-prod, so no regression-naming convention applies yet — that's deferred until real users start filing bugs.)
2. **`vos.ManagedMicroservice.Echo` has no test project.** Every other ManagedMicroservice has one.
3. **GUI tests are not in CI.** The Vitest suite and Puppeteer e2e provide no merge-gate signal.
4. **Mocking library split.** CLI and Metabolism use Moq; Tributary and Delta use NSubstitute. Small now, friction later for cross-service work.
5. **Coverage gate is enforced** as of Phase 3 (Task #5406). Per-assembly line + branch thresholds in `azure-pipelines.yml`'s gate step fail the build on regression. See *Coverage gate (Phase 3)* below for the gate shape + how to bump a threshold when coverage improves.
6. **`docs/DELIVERY.md`** sketches a `vos.ManagedMicroservice.Shared.Delivery` framework with its own test contract (Ack, dedup middleware, lifecycle). Not yet implemented; will reshape the test landscape when it lands.
7. **`docs/CONTRACT-VALIDATION.md`** describes the JSON Schema registry + validator landed in `vos.ManagedMicroservice.Shared/Contracts/` (Feature #5419 / Phase 1). Schemas + validator are dormant in Phase 1; `Tests/vos.ManagedMicroservice.Shared.Contracts.Tests/` exercises them end-to-end (self-validity + positive/negative fixture round-trips). Production coverage rolls into `vos.ManagedMicroservice.Shared`'s existing 100/100 threshold.

## Notable decisions in test-infrastructure shape

### `WebApplicationFactory<Program>` for minimal-API endpoint tests (Phase 2B precedent)

`docs/MICROSERVICE-TEMPLATE.md` (lines 134-142) prescribes `WebApplicationFactory<Program>` from `Microsoft.AspNetCore.Mvc.Testing` when a microservice's endpoint surface needs unit-level coverage. Metabolism was the first microservice to adopt the pattern (Phase 2B, Task #5403). The shape it landed on, modeled directly on the sibling `vos.Mycelium.Tests.BrokerWebApplicationFactory`:

- **`Tests/vos.ManagedMicroservice.Metabolism.Tests/MetabolismWebApplicationFactory.cs`** is a `WebApplicationFactory<Program>` subclass that implements `IAsyncLifetime` (workaround for sibling VillageOS Bug #5260 — sync-over-async deadlock in `CreateHost` under the XPlat Code Coverage collector on Windows CI).
- It sets `ASPNETCORE_ENVIRONMENT=Testing` plus the `METABOLISM_PORT` / `METABOLISM_BROKER_URL` / `METABOLISM_MODE` env vars in `InitializeAsync`, then clears them in `DisposeAsync`.
- **`EndpointMapperTests.cs`** exercises every endpoint via `factory.CreateClient()`.

Two minimal production-code changes were needed to make this work cleanly (the alternative would have been heavier test infrastructure that fights Program.cs's CLI-args contract):

- **`vos.ManagedMicroservice.Metabolism/Program.cs`** — added `public partial class Program { }` at the bottom (per `MICROSERVICE-TEMPLATE.md` instructions), and wrapped the SignalR-connect / broker-deregister `ApplicationStarted`/`ApplicationStopping` callbacks plus the Serilog file-sink configuration in `IsEnvironment("Testing")` guards. Production behavior is unchanged outside the Testing environment.
- **`vos.ManagedMicroservice.Metabolism/Configuration/CliArgs.cs`** — `Parse` now falls back to `METABOLISM_*` env vars when CLI args aren't supplied, so the test factory can inject config via env vars instead of synthetic CLI args. Production callers pass `--flag=value` as before; behavior is unchanged when all required flags are present in args.

**Why**: the documented choice was already `WebApplicationFactory<Program>` per the plan and template — the alternative (an inline `WebApplication` + `TestServer`, which I'd initially considered) would have deviated without a strong reason, and `vos.Mycelium`'s existing pattern proved the approach works in this org's .NET 10 / coverage-collector setup. Documenting here so 2C / 2D / future microservices follow the same shape.

**Coverage measurement note (Bug #5260 family)**: `reportgenerator` aggregates package-level coverage by averaging across declared source classes AND compiler-generated nested types (async state machines, lambda closures). The `[CompilerGenerated]` exclusion in `coverage.runsettings` filters them at coverlet level but their entries persist in the Cobertura XML — so the pkg-level number understates the real source coverage. For Metabolism after Phase 2B: every declared source file is at 95-100% line coverage, but the pkg-level report reads ~93% because async-state-machine partial coverage drags the average. The same caveat applies to any microservice that uses `async` heavily. Phase 3 (coverage gate enforcement) should either tighten the runsettings exclusions or compute thresholds on a per-source-file basis rather than pkg-average.

**SignalR hub callbacks excluded**: `vos.ManagedMicroservice.Metabolism/Services/BrokerClient.cs` has two SignalR callback bodies (the `RelationshipPropertyChanged` handler and the `Reconnected` handler). They only fire when a real broker hub delivers events — out of unit-test scope. Refactored into named methods (`HandleRelationshipPropertyChanged`, `HandleReconnected`) with `[ExcludeFromCodeCoverage]`, which `coverage.runsettings` already honors via `ExcludeByAttribute`.

### Phase 2C — same pattern, extended for endpoints that proxy outbound HTTP

Phase 2C (PR open against Task #5404) applied the same `WebApplicationFactory<Program>` shape to `vos.ManagedMicroservice.Tributary` (then named `vos.ManagedMicroservice.EndpointCaller`), with two refinements that future microservice 2x tasks should copy when they need outbound HTTP coverage:

- **`Tests/vos.ManagedMicroservice.Tributary.Tests/TributaryWebApplicationFactory.cs`** strips the default `DefaultHttpClientFactory` registration and replaces it with a `PerCallHttpClientFactory` that returns a fresh `HttpClient` per `CreateClient()` call. The single instance backed by `MockHttpMessageHandler` routes BOTH broker calls (`FindThingByNameAsync`, `GetEffectivePropertiesAsync`, `SetThingPropertyAsync`, `CreateThingAsync`, `CreateRelationshipAsync`) AND the outbound endpoint dispatched by `CallEndpointAsync` — same handler, request-URL-based routing inside each test. The per-call factory is required because `BrokerClientBase.CreateAuthenticatedClientAsync` mutates `client.Timeout` on every call, which throws `InvalidOperationException` on an already-used `HttpClient`; reusing a single instance only worked for tests that made exactly one broker call. The same caveat bit Phase 2D (`Delta`).
- **`HandlerCallback` is a per-test mutable `Func<HttpRequestMessage, HttpResponseMessage>`** on the factory; tests set it before the first `CreateClient()` call. This avoids the alternative of a parameterized factory constructor, which conflicts with `WebApplicationFactory<T>`'s expectation that the factory is parameterless.

Same `partial class Program { }` + Testing-env Serilog guard + `TRIBUTARY_*` env-var fallback pattern as Metabolism, minus the SignalR / deregister guards because Tributary doesn't have those.

Tributary's Program.cs lands at 94.3% (sub-95%); the remaining ~6% is the `cliArgs == null` exit path, the non-Testing Serilog file-sink branch, and the outer `catch (Exception)` around `app.Run()` — all minimal-API wireup that the plan flags for exclusion in Phase 3. ObservationIngestService lands at 89.1%; the remaining ~11% is jsonata edge-case defensive guards (e.g. the `IsNullOrWhiteSpace(rawResult)` branch is unreachable because `Jsonata.Net.Native` returns the literal string `"undefined"` for missing paths, not an empty string).

### Phase 2D — same pattern + seed-file bootstrap for Delta

Phase 2D (PR open against Task #5405) applied the same shape to `vos.ManagedMicroservice.Delta` (then named `vos.ManagedMicroservice.IntegrationRegistry`) with one new wrinkle worth noting for future microservice work:

- **`Tests/vos.ManagedMicroservice.Delta.Tests/DeltaWebApplicationFactory.cs`** writes a synthetic `seed.json` into `AppContext.BaseDirectory` in `InitializeAsync` and deletes it in `DisposeAsync`. `Program.cs`'s `LoadEndpointSeed` helper throws `InvalidOperationException` at host construction if no `seed.json` is found on any of its three candidate paths — and per CLAUDE.md "Seed files (`*.seed.json`, `seed.json`, `seeds/`) are runtime data and gitignored", so the file is never present in the test process's bin folder by default. The factory exposes a `SeedJson` string property that tests can override before `InitializeAsync` (used by `CoverageGapTests.MalformedSeedFactory` to drive the parse-failure catch + the final throw).
- Same `partial class Program {}` + Testing-env Serilog guard + `DELTA_*` env-var fallback as the other two microservices, including the `PerCallHttpClientFactory` from Phase 2C.

Delta's Program.cs lands at 89.9%; the remaining ~10% is the same minimal-API-wireup family (`cliArgs == null` exit, non-Testing Serilog branch, outer `catch (Exception)` around `app.Run`) **plus** the defensive outer `catch (Exception)` inside `HandleRegisterEndpointRequestAsync` which guards the whole handler against runtime exceptions that the broker-client's own per-method try/catches already swallow. That defensive catch is essentially unreachable from a unit test and falls under the same plan exclusion as the other minimal-API wireup.

### Coverage gate (Phase 3 / Task #5406)

`azure-pipelines.yml` enforces per-assembly line + branch coverage thresholds by invoking `Tools/Test-CoverageGate.ps1` after `dotnet test`. The script runs `reportgenerator` to merge the per-project Cobertura XMLs into one, parses the `<package>` elements, and compares each assembly's `line-rate` and `branch-rate` against an inline hashtable. Any drop below threshold fails the build.

The same script runs locally:

```powershell
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/local
pwsh Tools/Test-CoverageGate.ps1 -Reports "TestResults/local/**/coverage.cobertura.xml" -MergeOutput "TestResults/local/coverage-report"
```

This is the same command CI runs, so there's no "what does the YAML do that I can't reproduce locally" gap.

**Gate shape: per-assembly, no-regression.** Each assembly's threshold is set at its develop-tip value when the gate was first enabled, **rounded down to the nearest integer percent**. A sub-1pp fluctuation won't trip CI; a real regression will. The gate doesn't *push* anything upward — it locks in the current state. Per-class enforcement and aggressive `[ExcludeFromCodeCoverage]` annotations were considered and explicitly deferred so the gate's behavior matches what existing code already does (no production-code churn to ship the gate).

**Initial thresholds** (line / branch, integer percent — these are what's in the YAML; regenerate the baseline before raising):

| Assembly | Line | Branch |
|---|---|---|
| `vos.Auth.Shared` / `vos.ManagedMicroservice.Shared` / `vos.Tests.Shared` | 100 | 100 |
| `vos.Infrastructure` | 98 | 88 |
| `vos.Application` | 98 | 94 |
| `vos.ManagedMicroservice.Tributary` | 95 | 91 |
| `vos.Core` | 95 | 89 |
| `vos.CLI` | 94 | 89 |
| `vos.ManagedMicroservice.Delta` | 94 | 89 |
| `vos.ManagedMicroservice.Metabolism` | 85 | 81 |
| `vos.ManagedMicroservice.Echo` | 21 | 45 |

**How to raise a threshold.** Coverage improves → update the hashtable in `Tools/Test-CoverageGate.ps1` in the same PR that lands the test work. The gate is single-source-of-truth: there's no separate baseline file to forget. Reviewers can see the new threshold and the tests that justify it in one diff.

**How to add a new assembly.** New `vos.X.Tests/` project lands → on the next CI run the gate emits a warning ("no threshold for assembly 'vos.X'") and passes that assembly. The PR that adds the new test project should also add the new assembly's row to the hashtable using the freshly-measured per-assembly numbers, rounded down.

**Known coverage-measurement artifact.** `reportgenerator` aggregates package coverage by averaging across declared source classes AND compiler-generated nested types (async state machines, lambda closures). The `[CompilerGenerated]` attribute exclusion in `coverage.runsettings` filters them at coverlet level but their entries persist in the Cobertura XML — so the package-level numbers the gate checks understate real source coverage on async-heavy assemblies. `vos.ManagedMicroservice.Metabolism` is the clearest case: every declared source file is at 95-100% line coverage, but the package reads 85.5% because async-state-machine partial coverage drags the average. The gate threshold for Metabolism (85) reflects that artifact rather than its actual source-file quality. If the runsettings exclusions are tightened later, regenerate the baseline and raise the threshold in the same PR.

**Why per-assembly + no-regression instead of stricter shapes** — captured here for the next time this comes up:

- *Per-class enforcement* would catch drift inside an assembly that per-assembly averaging masks, but it requires explicit `[ExcludeFromCodeCoverage]` on every wireup branch we've documented as untestable. That's production-code churn for marginal coverage signal. Deferred.
- *Universal 95% line floor* would force immediate work on Echo (21%) and Metabolism (85%) — useful as a forcing function but blocks unrelated PRs until the gap is closed. The Echo gap is already tracked in Watch item #2; the gate doesn't need to be the reminder.
- *No gate at all* — what we had through Phase 2. Worked while a single reviewer was holding the line; doesn't scale.

The per-assembly no-regression shape is the smallest gate that catches silent drops on unrelated PRs without forcing production-code annotations or blocking work on documented gaps.

### Test-driven development as the working convention (Phase 4 / Task #5407)

Every code change in this repo follows TDD — write the failing test first, run it red, write the minimum production code to take it green, then refactor with the test as a safety net. The rule extends what `CLAUDE.md` > *Workflow* > *Before touching code* step 4 already required for bug fixes (`Bug<N>_<Scenario>` regression tests) to every code change — features and refactors included. The full red-green-refactor description lives in `CLAUDE.md` > *Workflow* > *Test-driven development*; this entry records *why* it landed as a phase deliverable rather than just an unwritten convention.

**Single carved-out exception:** single-line cosmetic fixes that cannot change runtime behavior — a typo in a string literal, a comment fix, a rename of a private symbol with no public surface. Anything that flips a condition, adjusts a calculation, changes a JSON shape, or touches an external contract gets a test first.

**Escape hatch for hard-to-test code** (UI, raycasting, time-based logic, async loops): extract the logic into a pure helper and test the helper. Same shape the bug-fix rule already used; now sanctioned for features too.

**Naming convention:**

- Bug regressions: `Bug<N>_<Scenario>` (pre-existing — kept).
- Everything else: `MethodOrClass_Scenario_ExpectedOutcome` (matches the existing test corpus across all the .NET test projects above).

**Relationship to the coverage gate.** The gate (Phase 3) and TDD (Phase 4) are complementary, not redundant. The gate catches numeric regressions: a PR that drops a package's `line-rate` below threshold fails CI. TDD catches *design-quality* regressions that pass the gate: features added with tests written after-the-fact tend to test what the code does rather than what the code should do, which the gate can't see. Shipping them together means the gate is the floor (no silent drops) and TDD is the working method that keeps the actual coverage well above the floor.

**Why this is documented in TEST-STATE.md as well as CLAUDE.md.** `CLAUDE.md` is gitignored per-developer in this repo, so the TDD section there propagates only to whoever has it locally. `docs/TEST-STATE.md` is tracked, mirrored to the AzDO wiki, and read by reviewers — adding the convention here makes it a contract reviewers can hold PRs to (test commits should precede or be visibly bundled with implementation commits; `git log` order is the verification surface).

## Open production issues surfaced by tests

Real bugs found while writing tests. Each should become an AzDO Bug when scheduled for fix.

1. **`vos.CLI/SeedCommandHandler.cs` — raw-string seed entries throw.** `ListLibrarySeedsAsync` calls `seed.GetStringOrDefault("Name", seed.ToString())` for each entry. `GetStringOrDefault` invokes `JsonElement.TryGetProperty` which throws when the element is a non-object (e.g. a raw JSON string). The outer `try`/`catch` in `ExecuteAsync` swallows the throw and emits a hostile `"Error: The requested operation cannot be performed on a JSON string element"` instead of the seed list. Surfaced by Phase 2A (PR #382); documented in `vos.CLI.Tests/SeedCommandHandlerTests.cs` (comment block, no regression test yet). Fix sketch: guard with `seed.ValueKind == JsonValueKind.String` and fall back to `seed.GetString()`, or push the guard into `JsonElementExtensions.GetStringOrDefault`. In practice today's broker returns objects, so this hasn't fired in production — the fallback is just dead code with a hostile failure mode.

## Candidate follow-ups

Each could become its own AzDO Task/Bug/Feature. Listed roughly in coverage-delta order (biggest blind spot first).

1. **Add direct tests for `vos.Core`** — temporal indexing, expected-ranges, and `VosModel` / `VosThing` / `VosRelationship` invariants. A new `Tests/vos.Core.Tests/` is the lowest-friction shape.
2. **Add direct tests for `vos.Application`** — `VosRelationshipService` handler invocation paths, `VosThingService` relation routing and JSON-fragment shapes.
3. **Add direct tests for `vos.Infrastructure`** — `ModelStore` concurrency and `VosModelProvider.FromJson` deserialization paths (properties, ranges, bindings, error cases).
4. **Add `Tests/vos.ManagedMicroservice.Echo.Tests/`** — CLI arg parsing, `BrokerClient` register/deregister, and the `/handle` / `/health` / `/stats` / `/shutdown` endpoints. Mirror the existing microservice test shape.
5. ~~**Expand `Tributary.Tests`** beyond the broker client — HTTP method dispatch, `TryGetEffectiveProperty` conflict handling, URL/URI validation, response-transform (JSONata), and `ObservationIngestService` parsing.~~ **Delivered** by Phase 2C (Task #5404) — see coverage table.
6. ~~**Expand `Delta.Tests`** beyond the broker client — `/handle` payload processing and lifecycle wiring.~~ **Delivered** by Phase 2D (Task #5405) — see coverage table.
7. **Wire `npm ci && npm test`** (and optionally `npm run test:coverage`) into `azure-pipelines.yml` so the GUI suite gates merges.
8. **Pick one mocking library** (Moq or NSubstitute) and converge the four .NET test projects.
9. **Decide coverage-gate policy** — enforce a threshold in `coverage.runsettings` / pipeline, or document that the "no coverage drop" rule is reviewer-enforced.

### Test-case backlog

Concrete test names that would close the gaps above (`MethodOrClass_Scenario_ExpectedOutcome` style). Not exhaustive — this is what an exploration pass turned up; add more as production code grows.

**`vos.Core`** (`vos.Core/VosModel.cs`, `VosRelationship.cs`, `ExpectedValues/*.cs`)

- `VosModel_AddRelationship_SelfReferencing_ThrowsInvalidOperationException`
- `VosModel_AddRelationship_DuplicateActiveTriple_ThrowsInvalidOperationException`
- `VosModel_AddRelationship_ReverseRelationshipExists_ThrowsInvalidOperationException`
- `VosModel_GetRelationshipsByPredicate_WithTimestamp_FiltersInactiveVersions`
- `VosModel_ChangeRelationshipPredicate_OldVersionStaysInOldIndex_NewVersionInNewIndex`
- `VosModel_DeleteRelationship_MarksDeletedTimestamp_DoesNotRemove`
- `VosRelationship_IsActiveAt_BeforeCreation_ReturnsFalse`
- `VosRelationship_IsActiveAt_AfterReplacement_ReturnsFalse`
- `VosRelationship_SetName_OverridesAutoGeneration_NameStaysOnPredicateChange`
- `ExpectedRange_Evaluate_WithDependencies_EvaluatesCriteria`
- `StateIndex_UpdateStateIndex_ObjectMovesFromOneStateToAnother_IndexesCorrectly`
- `StateIndex_GetThingsByStates_WithMultipleStateNames_ReturnsIntersection`

**`vos.Application`** (`vos.Application/VosRelationshipService.cs`, `VosThingService.cs`)

- `VosRelationshipService_NewRelationship_WithOnLoadTrue_FiresHandlerAndAwaits`
- `VosRelationshipService_NewRelationship_WithOnLoadFalse_FiresHandlerAsyncAndContinues`
- `VosRelationshipService_NewRelationship_HandlerThrows_LogsErrorButRelationshipPersists`
- `VosRelationshipService_NewRelationship_WithInvokeHandlerFalse_SkipsHandler`
- `VosThingService_AddRelation_SubjectMatch_AddsToSubjectRelations`
- `VosThingService_AddRelation_TargetMatch_AddsToTargetRelations`
- `VosThingService_ToJsonFragmentWithoutGeometry_ExcludesGeometryProperty`
- `VosThingService_GeometryFragment_ReturnsOnlyIdAndGeometry`

**`vos.Infrastructure`** (`vos.Infrastructure/ModelStore.cs`, `VosModelProvider.cs`)

- `ModelStore_Add_MultipleModels_StoresAllConcurrently`
- `ModelStore_Remove_ExistingModel_ReturnsTrue`
- `ModelStore_Remove_NonExistentModel_ReturnsFalse`
- `VosModelProvider_FromJson_CompleteModel_DeserializesThingsAndRelationships`
- `VosModelProvider_FromJson_MissingRelationships_ThrowsInvalidOperation`
- `VosModelProvider_AddPropertiesFromJson_VariousTypes_DeserializesCorrectly`
- `VosModelProvider_AddOwnRangesFromJson_RangeWithBindings_ParsesAllBindings`
- `VosModelProvider_ParsePropertyBinding_WithGuardCriteria_ExtractsDependencies`

**`vos.ManagedMicroservice.Echo`** (new test project)

- `CliArgs_Parse_ValidPortAndBrokerUrl_ReturnsValidCliArgs`
- `CliArgs_Parse_InvalidPort_ReturnsNull`
- `CliArgs_Parse_MissingBrokerUrl_ReturnsNull`
- `BrokerClient_RegisterAsync_SuccessfulRegistration_ReturnsTrue`
- `BrokerClient_RegisterAsync_AuthTokenFetchFails_ReturnsFalse`
- `EchoEndpoint_ValidJsonPayload_EchoesBackWithRequestCount`
- `HealthEndpoint_Get_ReturnsHealthyStatus`
- `StatsEndpoint_Get_ReturnsServiceStatsAndHandlerId`
- `ShutdownEndpoint_AuthEnabled_RequiresAuthorization`

**`vos.ManagedMicroservice.Tributary`** (expand existing project)

- `CallEndpointAsync_GetRequest_WithNoBody_OmitsContentHeader`
- `CallEndpointAsync_PostRequest_WithJsonBody_SendsContent`
- `CallEndpointAsync_HttpMethod_UnsupportedMethod_ReturnsValidationError`
- `TryGetEffectiveProperty_SingleMatch_ReturnsPropertyWithoutConflict`
- `TryGetEffectiveProperty_MultipleMatches_PopulatesConflictsList`
- `HandleEndpoint_MissingUrl_BadRequest`
- `HandleEndpoint_InvalidUri_BadRequest`
- `HandleEndpoint_SuccessResponseTransform_ReturnsTransformedContent`
- `ObservationIngestService_TryTransform_EmptyJsonataResult_ReturnsNull`
- `ObservationIngestService_CreateObservationsAsync_MissingObservedPredicate_CreatesOne`
- `ObservationIngestService_TryParseObservationPayloads_ArrayOfObjects_ParsesEach`
- `ObservationIngestService_TryParseObservation_MissingNameProperty_ReturnsError`

**`vos.ManagedMicroservice.Shared.BrokerClientBase`** (shared by all microservice tests; currently covered only as a side effect)

- `BrokerClientBase_GetTokenAsync_WithProvidedToken_ReturnsServiceToken`
- `BrokerClientBase_GetTokenAsync_NoTokenProvidedAndBrokerFails_ReturnsNull`
- `BrokerClientBase_CreateAuthenticatedClientAsync_NoToken_ThrowsInvalidOperation`
- `BrokerClientBase_DeregisterAsync_NoToken_LogsWarningAndReturns`
- `BrokerClientBase_DeregisterAsync_NonSuccessStatus_LogsWarningButContinues`
- `BrokerClientBase_HandlerIdGeneration_UniquePerInstance_DifferentGuidsGenerated`

## Verifying the snapshot

```powershell
# Run the full .NET suite and collect Cobertura coverage:
dotnet test --configuration Release `
  --collect:"XPlat Code Coverage" --settings coverage.runsettings `
  --results-directory TestResults\coverage-run `
  --logger "console;verbosity=minimal"
# Expect: all four .NET test projects discovered, all tests pass.
# One Cobertura XML per test project lands under TestResults\coverage-run\<guid>\.

# Run the GUI Vitest suite (not currently in CI):
Set-Location vos.GUI ; npm ci ; npm test
# Optional: GUI coverage via Vitest v8:
npm run test:coverage
# Reports land in vos.GUI\coverage\.
```
