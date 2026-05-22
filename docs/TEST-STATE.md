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
2. **`coverage.runsettings` filter elements were nested wrong.** coverlet's XPlat data collector wants `<ExcludeByFile>**/a.cs,**/b.cs</ExcludeByFile>` (comma-separated string content), not the nested `<File>` form Phase 0 (Task #5395) shipped. `<ExcludeByFile>` fixed under Task #5435 — all four microservices' `Program.cs` files were appearing in Cobertura with measured `line-rate` despite the wildcard that supposedly excluded them. `<Exclude>` and `<ExcludeByAttribute>` use the same nested form and may have the same bug, but test assemblies ARE absent from Cobertura so something is honored (or coverlet auto-excludes test projects); a full audit is deferred. (The old "Echo has no test project" item resolved under Phase 1E / Task #5400 — Echo's test project is now the canonical microservice template per `docs/MICROSERVICE-TEMPLATE.md`.)
3. **GUI tests are not in CI.** The Vitest suite and Puppeteer e2e provide no merge-gate signal.
4. **Mocking library split.** CLI and Metabolism use Moq; Tributary and Delta use NSubstitute. Small now, friction later for cross-service work.
5. **Coverage gate is enforced** as of Feature #5433 (Task #5434). Diff coverage on the PR's added/modified executable lines against an 85% threshold, computed by `Tools/Test-CoverageGate.ps1`. Replaced the Phase 3 per-assembly absolute-threshold gate (Task #5406) which produced false failures on dev because coverage measurement is noisy across environments. See *Coverage gate* below for the shape + tuning.
6. **`docs/DELIVERY.md`** sketches a `vos.ManagedMicroservice.Shared.Delivery` framework with its own test contract (Ack, dedup middleware, lifecycle). Not yet implemented; will reshape the test landscape when it lands.
7. **`docs/CONTRACT-VALIDATION.md`** describes the JSON Schema registry + validator landed in `vos.ManagedMicroservice.Shared/Contracts/` (Feature #5419 / Phase 1) and the request-pipeline middleware landed in `vos.ManagedMicroservice.Shared/Middleware/` (Feature #5426 / Phase 2). Metabolism is the first adopter — `Tests/vos.ManagedMicroservice.Metabolism.Tests/ContractValidationIntegrationTests.cs` exercises the end-to-end pipeline (valid + `Required` / `AdditionalProperties` / `Type` / `Malformed` failure cases + `/health` pass-through). Unit-level middleware tests live in `Tests/vos.ManagedMicroservice.Shared.Tests/Middleware/`. Production coverage rolls into `vos.ManagedMicroservice.Shared`'s existing 100/100 threshold.

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

**Coverage measurement note (Bug #5260 family)**: `reportgenerator` aggregates package-level coverage by averaging across declared source classes AND compiler-generated nested types (async state machines, lambda closures). The `[CompilerGenerated]` exclusion in `coverage.runsettings` filters them at coverlet level but their entries persist in the Cobertura XML — so the pkg-level number understates the real source coverage. For Metabolism after Phase 2B: every declared source file is at 95-100% line coverage, but the pkg-level report reads ~93% because async-state-machine partial coverage drags the average. The same caveat applies to any microservice that uses `async` heavily. Under the diff-coverage gate (Task #5434) this no longer affects the gate signal — the gate folds duplicate `<line>` entries by max-hits, so one hit anywhere counts as covered; only the displayed snapshot percentages here are still affected.

**SignalR hub callbacks excluded**: `vos.ManagedMicroservice.Metabolism/Services/BrokerClient.cs` has two SignalR callback bodies (the `RelationshipPropertyChanged` handler and the `Reconnected` handler). They only fire when a real broker hub delivers events — out of unit-test scope. Refactored into named methods (`HandleRelationshipPropertyChanged`, `HandleReconnected`) with `[ExcludeFromCodeCoverage]`, which `coverage.runsettings` already honors via `ExcludeByAttribute`.

### Phase 2C — same pattern, extended for endpoints that proxy outbound HTTP

Phase 2C (PR open against Task #5404) applied the same `WebApplicationFactory<Program>` shape to `vos.ManagedMicroservice.Tributary` (then named `vos.ManagedMicroservice.EndpointCaller`), with two refinements that future microservice 2x tasks should copy when they need outbound HTTP coverage:

- **`Tests/vos.ManagedMicroservice.Tributary.Tests/TributaryWebApplicationFactory.cs`** strips the default `DefaultHttpClientFactory` registration and replaces it with a `PerCallHttpClientFactory` that returns a fresh `HttpClient` per `CreateClient()` call. The single instance backed by `MockHttpMessageHandler` routes BOTH broker calls (`FindThingByNameAsync`, `GetEffectivePropertiesAsync`, `SetThingPropertyAsync`, `CreateThingAsync`, `CreateRelationshipAsync`) AND the outbound endpoint dispatched by `CallEndpointAsync` — same handler, request-URL-based routing inside each test. The per-call factory is required because `BrokerClientBase.CreateAuthenticatedClientAsync` mutates `client.Timeout` on every call, which throws `InvalidOperationException` on an already-used `HttpClient`; reusing a single instance only worked for tests that made exactly one broker call. The same caveat bit Phase 2D (`Delta`).
- **`HandlerCallback` is a per-test mutable `Func<HttpRequestMessage, HttpResponseMessage>`** on the factory; tests set it before the first `CreateClient()` call. This avoids the alternative of a parameterized factory constructor, which conflicts with `WebApplicationFactory<T>`'s expectation that the factory is parameterless.

Same `partial class Program { }` + Testing-env Serilog guard + `TRIBUTARY_*` env-var fallback pattern as Metabolism, minus the SignalR / deregister guards because Tributary doesn't have those.

Tributary's Program.cs lands at 94.3% (sub-95%); the remaining ~6% is the `cliArgs == null` exit path, the non-Testing Serilog file-sink branch, and the outer `catch (Exception)` around `app.Run()` — all minimal-API wireup that the plan flags for exclusion in Phase 3. ObservationIngestService lands at 89.1%; the remaining ~11% is jsonata edge-case defensive guards (e.g. the `IsNullOrWhiteSpace(rawResult)` branch is unreachable because `Jsonata.Net.Native` returns the literal string `"undefined"` for missing paths, not an empty string).

### Phase 2D — same pattern + seed-file bootstrap for Delta

Phase 2D (PR open against Task #5405) applied the same shape to `vos.ManagedMicroservice.Delta` (then named `vos.ManagedMicroservice.IntegrationRegistry`) with one new wrinkle worth noting for future microservice work:

- **`Tests/vos.ManagedMicroservice.Delta.Tests/DeltaWebApplicationFactory.cs`** writes a synthetic `seed.json` into `AppContext.BaseDirectory` in `InitializeAsync` and deletes it in `DisposeAsync`. `EndpointSeedLoader.LoadDefault` (extracted from Program.cs under Task #5436) throws `InvalidOperationException` at host construction if no `seed.json` is found on any of its three candidate paths — and per CLAUDE.md "Seed files (`*.seed.json`, `seed.json`, `seeds/`) are runtime data and gitignored", so the file is never present in the test process's bin folder by default. The factory exposes a `SeedJson` string property that tests can override before `InitializeAsync`; `Tests/.../EndpointSeedBootTests.cs` uses it inline (`new DeltaWebApplicationFactory { SeedJson = "{ malformed" }`) to pin the host-construction failure contract.
- Same `partial class Program {}` + Testing-env Serilog guard + `DELTA_*` env-var fallback as the other two microservices, including the `PerCallHttpClientFactory` from Phase 2C.

Delta's Program.cs lands at 89.9%; the remaining ~10% is the same minimal-API-wireup family (`cliArgs == null` exit, non-Testing Serilog branch, outer `catch (Exception)` around `app.Run`) **plus** the defensive outer `catch (Exception)` inside `HandleRegisterEndpointRequestAsync` which guards the whole handler against runtime exceptions that the broker-client's own per-method try/catches already swallow. That defensive catch is essentially unreachable from a unit test and falls under the same plan exclusion as the other minimal-API wireup.

### Coverage gate (Feature #5433 / Task #5434)

`azure-pipelines.yml` enforces a **diff coverage** gate by invoking `Tools/Test-CoverageGate.ps1` after `dotnet test`. The script merges per-project Cobertura XMLs via `reportgenerator`, parses `git diff --unified=0 $(git merge-base origin/develop HEAD)..HEAD`, and intersects the PR's added/modified lines with `<line hits=...>` entries in the merged Cobertura. Patch coverage below the threshold (default 85%) fails the build.

The same script runs locally:

```powershell
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/local
pwsh Tools/Test-CoverageGate.ps1 -Reports "TestResults/local/**/coverage.cobertura.xml" -MergeOutput "TestResults/local/coverage-report"
```

Same command CI runs — no "what does the YAML do that I can't reproduce locally" gap.

**Gate shape: patch coverage, single threshold.** A changed line counts toward the gate's denominator only if it has a `<line>` entry in the merged Cobertura. That naturally drops:

- Braces, comments, blank lines — coverlet doesn't emit `<line>` entries for non-executable lines.
- Files excluded via `coverage.runsettings` `ExcludeByFile` (e.g. `**/vos.ManagedMicroservice.*/Program.cs` minimal-API wireup).
- Test-project sources (excluded by `ExcludeByModulePath` in runsettings).
- Pure deletions, binary-file changes, and rename-without-edit — no `+` lines in the diff.

A docs-only PR (no executable .NET changes) hits a clean "no executable changes to gate" pass branch.

**Why this replaced the Phase 3 per-assembly thresholds (Task #5406).** The old gate baked dev-tip percentages into `Tools/Test-CoverageGate.ps1` as per-assembly absolute thresholds. CI on `windows-latest` reproduced them; the maintainer's dev box did not, because coverage measurement is noisy across environments — parallel xUnit ordering, env-var leakage between tests sharing a collection, stale `TestResults/` artefacts merged into reruns. The gate produced false failures locally with no real regression. Diff coverage is environment-independent: only changed lines are measured, so noise in unchanged code is invisible to the gate.

**Tuning the threshold.** The default is 85%, set in the script's `param()` block. To raise it, pass `-Threshold 90` (or higher) when invoking the script, and bake the new value into the CI step's `arguments`. There's no per-assembly knob — a single number applies to whatever the PR touched.

**Adding a new assembly.** Nothing to do. The first time a new assembly's source is touched, the gate measures coverage of those changed lines against the same threshold.

**Local invocation tips.**

- Use `--results-directory TestResults/local` (or any fresh dir) when running `dotnet test` so prior runs' Cobertura XMLs aren't merged into the current run. Stale artefacts in `TestResults/` were a meaningful source of fluctuation under the old gate; the new gate inherits the same input pipe.
- The gate uses `origin/develop` as the base ref by default. If you're working off a different upstream, pass `-BaseRef origin/main` (or similar). CI fetches full history (`fetchDepth: 0`) so this resolves correctly there.
- Pure-deletion PRs (e.g. removing dead code) pass with "no executable changes to gate" because the diff has no `+` lines.

**Async-state-machine artefact** *(carried over from Phase 3 because it still matters at the line-hit level)*. coverlet emits multiple `<line>` entries for the same source line when async state machines + lambda closures expand into compiler-generated types. The gate folds duplicates by taking the max `hits` value across all entries for a given `(file, line)`, so one hit anywhere = the line counts as covered. The artefact no longer drags package averages because the gate doesn't read package averages.

**Total-coverage signal.** Not gated. Still computed and published as the Cobertura artefact by the pipeline; reviewers can inspect it but it doesn't fail the build.

### Test-driven development as the working convention (Phase 4 / Task #5407)

Every code change in this repo follows TDD — write the failing test first, run it red, write the minimum production code to take it green, then refactor with the test as a safety net. The rule extends what `CLAUDE.md` > *Workflow* > *Before touching code* step 4 already required for bug fixes (`Bug<N>_<Scenario>` regression tests) to every code change — features and refactors included. The full red-green-refactor description lives in `CLAUDE.md` > *Workflow* > *Test-driven development*; this entry records *why* it landed as a phase deliverable rather than just an unwritten convention.

**Single carved-out exception:** single-line cosmetic fixes that cannot change runtime behavior — a typo in a string literal, a comment fix, a rename of a private symbol with no public surface. Anything that flips a condition, adjusts a calculation, changes a JSON shape, or touches an external contract gets a test first.

**Escape hatch for hard-to-test code** (UI, raycasting, time-based logic, async loops): extract the logic into a pure helper and test the helper. Same shape the bug-fix rule already used; now sanctioned for features too.

**Naming convention:**

- Bug regressions: `Bug<N>_<Scenario>` (pre-existing — kept).
- Everything else: `MethodOrClass_Scenario_ExpectedOutcome` (matches the existing test corpus across all the .NET test projects above).

**Relationship to the coverage gate.** The gate (diff coverage, Feature #5433 / Task #5434) and TDD (Phase 4 / Task #5407) are complementary, not redundant. The gate catches numeric regressions: a PR whose patch coverage falls below the threshold fails CI. TDD catches *design-quality* regressions that pass the gate: features added with tests written after-the-fact tend to test what the code does rather than what the code should do, which the gate can't see. Shipping them together means the gate is the floor (no untested changed lines) and TDD is the working method that keeps the actual coverage well above the floor.

**Why this is documented in TEST-STATE.md as well as CLAUDE.md.** `CLAUDE.md` is gitignored per-developer in this repo, so the TDD section there propagates only to whoever has it locally. `docs/TEST-STATE.md` is tracked, mirrored to the AzDO wiki, and read by reviewers — adding the convention here makes it a contract reviewers can hold PRs to (test commits should precede or be visibly bundled with implementation commits; `git log` order is the verification surface).

### Test-quality convention (Feature #5433 / Task #5437)

Every test must encode an **observable contract**: if production code is broken in a way callers can detect, the test must fail. Tests whose only assertion is `NotThrow()`, substring-matches on help-text or error-message strings, or pins down a specific switch arm / early-return without checking what came out are the anti-pattern. They contribute to coverage percentages but pin implementation details — they break on refactors that preserve behaviour, and they pass on regressions that break behaviour.

The retired-under-Task-#5437 `CoverageGapTests.cs` corpus is the worked example of the anti-pattern. Concrete signals the triage caught:

- File or test named for the metric or branch it raises (`CoverageGapTests`, `*_TakesEarlyReturn*`, `*_HitsCatchBlock*`).
- Theory or `[Fact]` whose body is one `NotThrow()` call.
- Substring assertion on a string the production code generates (help text, error message format) where the test doesn't care about the rest of the contract.
- Comments naming production-file line numbers (e.g. `// (lines 137, 151-155, 159-163)`). Line numbers rot the first time anyone reformats the file.

When a test asserts an outcome a refactor must preserve (return value, exception type, exception message, observable side effect, state change), it belongs in a file named for the unit-under-test. When a test exists only to make a number go up, delete it.

### EnvVarScope for env-var-mutating tests (Task #5437)

Tests that need to set process environment variables (e.g. Delta/Tributary auth-wireup tests that need `<SERVICE>_SIGNING_KEY` set before host construction) use the `EnvVarScope` IDisposable defined in each microservice's test project. The scope sets vars in its constructor and restores their prior values on `Dispose`. Use it with `using var env = new EnvVarScope(("X", "v"), ...)` inside the test body.

The retired pattern — a subclass of the WebApplicationFactory that `new`-shadowed `InitializeAsync`/`DisposeAsync` to layer env-var setup on top of the base — looks correct but breaks under `await using` dispatch: `IAsyncDisposable.DisposeAsync()` lands on the base `WebApplicationFactory<T>` method, skipping both layers of env-var cleanup. The leak then bleeds into the next test in the collection.

Lifted to `vos.Tests.Shared` is the natural next step when a third service needs it; until then it's duplicated locally in `Tests/vos.ManagedMicroservice.Delta.Tests/EnvVarScope.cs` and `Tests/vos.ManagedMicroservice.Tributary.Tests/EnvVarScope.cs`.

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
