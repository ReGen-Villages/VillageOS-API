# Next Steps

Consolidated backlog from `DELIVERY.md`. Source doc remains authoritative for details. (The test-coverage initiative was delivered through Phases 0-4 / Tasks #5396-5407 — see `docs/TEST-STATE.md`. The river-ecosystem rename `IntegrationRegistry → Delta` / `EndpointCaller → Tributary` was delivered as Task #5416; the source plan `RENAME-DELTA-TRIBUTARY.md` has been deleted now that the rename shipped.)

## 1. Delivery contract — extend `vos.ManagedMicroservice.Shared`

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
- Delta, Tributary, Metabolism. Each PR also lands service-specific ACK codes (e.g. Delta 409 on existing endpoint thing; Metabolism 429 at sim cap).

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
