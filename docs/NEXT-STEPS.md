# Next Steps

Consolidated backlog from `RENAME-DELTA-TRIBUTARY.md` and `DELIVERY.md`. Source docs remain authoritative for details. (The test-coverage initiative previously listed here as #1 was delivered through Phases 0-4 / Tasks #5396-5407; the source plan `TEST-COVERAGE-PLAN.md` has been deleted now that every phase shipped. See `docs/TEST-STATE.md` for the current snapshot, the coverage-gate design, and the TDD working convention.)

## 1. Rename microservices (river-ecosystem aesthetic)

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

## 2. Delivery contract — extend `vos.ManagedMicroservice.Shared`

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
