# Test State

A living snapshot of how unit tests are organized, what's covered, and where the gaps are. Update this when the test landscape changes — do **not** put hardcoded test counts here (they rot on the next PR).

## Where tests live

.NET test projects (all `net10.0`, all listed in `VillageOS-API.sln`):

| Project | Path | Mocking | Production code covered |
|---|---|---|---|
| `vos.CLI.Tests` | `vos.CLI.Tests/` | Moq | `vos.CLI` only — the Application/Core/Infrastructure layers are mocked at the seam, so they get no transitive coverage from this project (verified against Cobertura output) |
| `vos.ManagedMicroservice.Metabolism.Tests` | `Tests/vos.ManagedMicroservice.Metabolism.Tests/` | Moq + `MockHttpMessageHandler` | Metabolism (consumes/produces simulation lifecycle and the broker client) |
| `vos.ManagedMicroservice.EndpointCaller.Tests` | `Tests/vos.ManagedMicroservice.EndpointCaller.Tests/` | NSubstitute + `MockHttpMessageHandler` | EndpointCaller (broker client only) |
| `vos.ManagedMicroservice.IntegrationRegistry.Tests` | `Tests/vos.ManagedMicroservice.IntegrationRegistry.Tests/` | NSubstitute + `MockHttpMessageHandler` | IntegrationRegistry (broker client only) |
| `vos.Auth.Shared.Tests` | `Tests/vos.Auth.Shared.Tests/` | None (no mocks needed — pure helpers + extension methods) | `vos.Auth.Shared` (`ServiceTokenValidator`, `HandlerAuthExtensions`, constants) |
| `vos.Microservice.Shared.Tests` | `Tests/vos.Microservice.Shared.Tests/` | `NullLogger` + `MockHttpMessageHandler` (no mocking-library dependency) | `vos.Microservice.Shared` (`BrokerClientBase` via a thin `TestableBrokerClient` subclass, `HttpMethodValidator`, `RequiredPropertyValidator`) |
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
| `vos.ManagedMicroservice.Metabolism` | ~51% | ~74% | `vos.ManagedMicroservice.Metabolism.Tests` |
| `vos.Microservice.Shared` | 100% | 100% | `Tests/vos.Microservice.Shared.Tests/` (Phase 1F, Task #5401); previously ~31% as a side effect of the three microservice test runs |
| `vos.ManagedMicroservice.EndpointCaller` | ~23% | ~22% | `vos.ManagedMicroservice.EndpointCaller.Tests` (broker-client only) |
| `vos.ManagedMicroservice.IntegrationRegistry` | ~19% | ~19% | `vos.ManagedMicroservice.IntegrationRegistry.Tests` (broker-client only) |
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
4. **Mocking library split.** CLI and Metabolism use Moq; EndpointCaller and IntegrationRegistry use NSubstitute. Small now, friction later for cross-service work.
5. **No coverage thresholds enforced.** CLAUDE.md says "Coverage must improve or hold across every PR." but the pipeline only collects — compliance currently relies on reviewer attention.
6. **`docs/DELIVERY.md`** sketches a `vos.ManagedMicroservice.Shared.Delivery` framework with its own test contract (Ack, dedup middleware, lifecycle). Not yet implemented; will reshape the test landscape when it lands.

## Open production issues surfaced by tests

Real bugs found while writing tests. Each should become an AzDO Bug when scheduled for fix.

1. **`vos.CLI/SeedCommandHandler.cs` — raw-string seed entries throw.** `ListLibrarySeedsAsync` calls `seed.GetStringOrDefault("Name", seed.ToString())` for each entry. `GetStringOrDefault` invokes `JsonElement.TryGetProperty` which throws when the element is a non-object (e.g. a raw JSON string). The outer `try`/`catch` in `ExecuteAsync` swallows the throw and emits a hostile `"Error: The requested operation cannot be performed on a JSON string element"` instead of the seed list. Surfaced by Phase 2A (PR #382); documented in `vos.CLI.Tests/SeedCommandHandlerTests.cs` (comment block, no regression test yet). Fix sketch: guard with `seed.ValueKind == JsonValueKind.String` and fall back to `seed.GetString()`, or push the guard into `JsonElementExtensions.GetStringOrDefault`. In practice today's broker returns objects, so this hasn't fired in production — the fallback is just dead code with a hostile failure mode.

## Candidate follow-ups

Each could become its own AzDO Task/Bug/Feature. Listed roughly in coverage-delta order (biggest blind spot first).

1. **Add direct tests for `vos.Core`** — temporal indexing, expected-ranges, and `VosModel` / `VosThing` / `VosRelationship` invariants. A new `Tests/vos.Core.Tests/` is the lowest-friction shape.
2. **Add direct tests for `vos.Application`** — `VosRelationshipService` handler invocation paths, `VosThingService` relation routing and JSON-fragment shapes.
3. **Add direct tests for `vos.Infrastructure`** — `ModelStore` concurrency and `VosModelProvider.FromJson` deserialization paths (properties, ranges, bindings, error cases).
4. **Add `Tests/vos.ManagedMicroservice.Echo.Tests/`** — CLI arg parsing, `BrokerClient` register/deregister, and the `/handle` / `/health` / `/stats` / `/shutdown` endpoints. Mirror the existing microservice test shape.
5. **Expand `EndpointCaller.Tests`** beyond the broker client — HTTP method dispatch, `TryGetEffectiveProperty` conflict handling, URL/URI validation, response-transform (JSONata), and `ObservationIngestService` parsing.
6. **Expand `IntegrationRegistry.Tests`** beyond the broker client — `/handle` payload processing and lifecycle wiring.
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

**`vos.ManagedMicroservice.EndpointCaller`** (expand existing project)

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

**`vos.Microservice.Shared.BrokerClientBase`** (shared by all microservice tests; currently covered only as a side effect)

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
