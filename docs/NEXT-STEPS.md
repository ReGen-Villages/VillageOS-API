# Next Steps

Consolidated backlog from `TEST-COVERAGE-PLAN.md`, `RENAME-DELTA-TRIBUTARY.md`, and `DELIVERY.md`. Source docs remain authoritative for details.

## 1. Test coverage to 95%+ (.NET only)

One AzDO Feature + one Task per project. Target: 95% line / 85% branch with documented exclusions.

**Phase 0 — Foundation**
- Create umbrella Feature in AzDO; child Tasks linked via `-ParentId`.
- New `Tests/vos.Tests.Shared/` (net10.0) holding canonical `MockHttpMessageHandler` + `TestHttpClientFactory`; migrate three duplicates.
- Update `coverage.runsettings`: exclude `*Tests*.dll`, `**/Program.cs`, compiler-generated closures.

**Phase 1 — New test projects (0% coverage assemblies)**
- 1A `Tests/vos.Core.Tests/` — domain, indices, ranges, parser, cycle detection.
- 1B `Tests/vos.Application.Tests/` — services + JSON helpers.
- 1C `Tests/vos.Infrastructure.Tests/` — `ModelStore` concurrency, `VosModelProvider.FromJson`.
- 1D `Tests/vos.Auth.Shared.Tests/` — token validator, DI extensions, constants.
- 1E `Tests/vos.ManagedMicroservice.Echo.Tests/` — CliArgs, BrokerClient, endpoints.
- 1F `Tests/vos.Microservice.Shared.Tests/` — `BrokerClientBase`, validators.

**Phase 2 — Expand partial-coverage projects to ≥95%**
- 2A `vos.CLI.Tests/` — add `BrokerStatusCommandHandlerTests`, `UserCommandHandlerTests`; audit branches.
- 2B Metabolism.Tests — `EndpointMapperTests` via `WebApplicationFactory<Program>`; direct `SimulationEntry`.
- 2C EndpointCaller.Tests — call paths, property conflicts, JSONata transform, ingest service.
- 2D IntegrationRegistry.Tests — `/handle`, `/register`, seed parsing, conflict + compensation.

**Phase 3 — Coverage gate**
- Per-assembly `<MinimumCoverage>` in `coverage.runsettings`.
- Add `reportgenerator` + gate step to `azure-pipelines.yml`; publish HTML artifact.
- Update CLAUDE.md CI section.

**Phase 4 — TDD docs + memory**
- Add "Test-driven development" section to CLAUDE.md under Workflow.
- Mirror to AzDO + GitHub wikis.
- Save `feedback_tdd.md` memory + MEMORY.md entry.

Sequencing: Phase 0 first. Phase 1 (A–F) and Phase 2 (A–D) parallel. Phase 3 lands after 1D or 1F validates the gate. Phase 4 anytime.

## 2. Rename microservices (river-ecosystem aesthetic)

`IntegrationRegistry → Delta`, `EndpointCaller → Tributary`. Rename-only, no behavioral changes. Broker companion PR pending in `ReGenVillages/VillageOS`.

**Pre-flight**
- AzDO Task required; capture `$workItemId`.
- No backwards-compat shims (delete old strings).

**Step A — IntegrationRegistry → Delta**
- A1 `git mv` folders (main + tests).
- A2 Rename `.csproj` files.
- A3 Update `<AssemblyName>`/`<RootNamespace>` if present; fix `<ProjectReference>` in test csproj.
- A4 Update `VillageOS-API.sln` (keep GUIDs).
- A5 Rewrite namespaces/usings in all `.cs` files.
- A6 `Program.cs` identity strings: log prefix `delta-.log`, `Service`=`"Delta"`, `/health`=`"Delta"`, shutdown + fatal logs.
- A7 launchSettings profile key.
- A8 `vos.Microservice.Shared/BrokerClientBase.cs` XML doc reference.

**Step B — EndpointCaller → Tributary**
- Mirror Steps A1–A7 with `Tributary` naming. Files include `ObservationIngestService.cs`, `IEndpointBrokerClient.cs`.

**Step C — Documentation sweep**
- C1 In-repo: `README.md`, `CLAUDE.md`, `docs/DELIVERY.md`, `docs/TEST-STATE.md`, per-service READMEs.
- C2 DevOps wiki: mirrored pages via `Invoke-RestMethod` + `If-Match` ETag.
- C3 GitHub wiki: equivalent pages.

**Step D** — Append `CLAUDE.md` to `.gitignore`.

**Step E** — Dead-code sweep: grep `IntegrationRegistry`, `EndpointCaller`, kebab/lowercase variants. Zero hits outside `bin/obj/.git/logs/`.

**Step F** — `dotnet restore && dotnet build && dotnet test` green.

**Step G** — Smoke test: launch each renamed service, verify `/health` payload + log filenames.

**Step H** — Commit (HEREDOC), push, `Set-AzDoPrWorkItemLink`. PR description must note broker companion PR pending.

## 3. Delivery contract — extend `vos.ManagedMicroservice.Shared`

Add idempotent receive-side delivery contract + shared host bootstrap. Needs a Feature work item; each phase below is a child Bug/Feature with Test Cases.

**Phase 0 — Rename**
- `vos.Microservice.Shared` → `vos.ManagedMicroservice.Shared`. Folder, csproj, namespace, every `using` + `<ProjectReference>` across services + tests + sln.

**Phase A — Add `vos.ManagedMicroservice.Shared.Delivery` namespace**
- `MicroserviceCliArgs` base + `CliArgsParser.Parse<T>`.
- `AddMicroserviceLogging`, `AddMicroserviceAuth`, `MapStandardEndpoints`, `UseBrokerLifecycle`.
- `Ack` helpers: `Ok` 200 / `Accepted` 202 / `Duplicate` 409 / `TooBusy` 429 / `Failed` 500 / `Refused` 501.
- `UseDeliveryReceive` middleware + `IDeliveryReceiveCache` (in-memory LRU).
- `RequireDeliveryId()` route extension.
- In-flight draining in `ApplicationStopping` (default 5s budget → 503).
- Health envelope: `status`, `service`, `uptimeSeconds`, `requestsReceived`, `dedupedRequests`, `lastRequestUtc`, `extras`.

**Phase B — Migrate Echo first**
- Shrink `Program.cs` to the ~12-line shape; delete `Configuration/CliArgs.cs`.
- Update `MICROSERVICE_GUIDE.md` in same PR.
- Test: duplicate `X-Delivery-Id` returns cached body.

**Phase C — Migrate remaining services (one PR each)**
- IntegrationRegistry/Delta, EndpointCaller/Tributary, Metabolism. Each PR also lands service-specific ACK codes (e.g. Delta 409 on existing endpoint thing; Metabolism 429 at sim cap).

**Phase D — `IDeliveryDispatch` (in-process retry over `BrokerClientBase`)**
- Lands when first needed (likely Tributary observation-ingest).
- Contract tests: retry on 500, no-retry on 501, treat 409 as success.

**Phase E (optional)** — Persistent dispatch variant only if real durability need surfaces.

**Required tests in shared project**
- `Ack.*` codes, content, body shape.
- `UseDeliveryReceive` pass-through / miss / hit / LRU / TTL.
- `RequireDeliveryId()` → 400 on missing header.
- `UseBrokerLifecycle` startup register, shutdown deregister + drain + 503 + `StopApplication`.
- `MapStandardEndpoints` `/health` envelope + `/shutdown` drain path.
- `MicroserviceCliArgs.Parse<T>` validation + subclass flags.
- `IDeliveryDispatch` contract.

**Open**
- Cache pluggability (v1 in-memory only).
- Fold `/stats` into `/health` extras.
- Unify per-service `BrokerClient` — separate refactor.
- Phase E only on real need.
