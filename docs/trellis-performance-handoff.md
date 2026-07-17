# Trellis performance under the sim — investigation, fix, and remaining work

**Date:** 2026-07-14
**Context:** `./run-sim.sh` (actor scenario, a sustained high write rate). Trellis
(the Operations dashboard) is responsive when the sim starts but degrades as it runs.
**Status:** Root cause found. Client-side "quick wins" implemented, tested, and
deployed — they resolve the reported degradation. The larger architectural
"real fixes" are **not** done and are specified below for a later pick-up.

---

## 1. Diagnosis

Measured mid-run against a live sim:

| Signal | Value |
| --- | --- |
| `/api/things` payload | ~10 MB, served in ~40 ms |
| `/api/relationships` payload | ~15 MB, served in ~40 ms |
| Things in model | 16,926 and climbing |
| Relationships in model | 56,134 and climbing |

**The server is fine.** The degradation is entirely client-side. The whole model
is held in the browser (`modelStore`) and grows without bound as the sim runs;
the Operations dashboard resolves every widget client-side over that model. So
per-frame cost scales as `event_rate × model_size × widget_count`, and
`model_size` grows continuously while `event_rate` stays high. Snappy at N≈1k,
janky at N≈70k.

Four compounding drivers (all pre-fix):

1. **O(N) store upserts on every SSE event.** `upsertThing` / `upsertRelationship`
   did a `.some()` scan **plus a full array copy** on every `ThingCreated` /
   `RelationshipCreated`. At a high event rate over tens of thousands of entries,
   each event allocated a fresh 10–15 MB array — main-thread + GC saturation.
2. **The model index rebuilt ~3× per change.** Every new array reference re-ran
   three separate memos over the entire model — `discoverDashboards`,
   `buildModelIndex`, and `useResolveContext` — each allocating several Maps/Sets
   across all ~73k entries.
3. **Every widget re-resolved on every `PropertyChanged`.** `REFRESH_EVENTS`
   bumped a `nonce` on `PropertyChanged` / `StatesChanged` / etc. (the
   highest-rate events the model emits). Worse, `nonce` was a dependency of the
   `buildModelIndex` memo inside `useResolveContext`, so **each event rebuilt the
   full 73k-entry index even when the model had not changed**, then re-resolved
   every widget (each `aggregate` / `stateCount` binding walks the full
   `is`-hierarchy via `thingsOfArchetype`).
4. **GC pressure** from all the full-array copies and Map rebuilds added pauses on
   top.

---

## 2. What was fixed (the "quick wins") — DONE

All changes are in **`VillageOS-API/vos.Trellis`** and ship on
`perf/5958-trellis-batched-model-updates` (work item #5958). The full frontend
suite is green. Built into the running Mycelium's `wwwroot`, so a browser reload
picks them up.

| File | Change |
| --- | --- |
| `src/stores/modelStore.ts` | Added `applyBatch(ModelBatch)` — applies a coalesced batch (upserts + removals + property updates, things and relationships) in **one** store write, rebuilding each array at most once via a `Map` (O(N + batch), not O(N) per event). Collections with no changes keep their identity (no needless rebuild). |
| `src/hooks/useModelData.ts` | Live SSE handlers now **buffer** into pending sets/maps and **flush on a debounce** (`FLUSH_DEBOUNCE_MS = 150`). A burst of hundreds of structural events becomes a single `applyBatch`. Hydration fetches run with bounded concurrency (`HYDRATE_CONCURRENCY = 8`) and keep the existing single-retry behavior (Bug #5940). |
| `src/api/dashboardApi.ts` | Added `discoverDashboardsFromIndex(idx)` so callers can reuse one index instead of building a throwaway one inside discovery. `discoverDashboards(things, rels)` kept as a thin wrapper (used by tests). |
| `src/hooks/useDashboard.ts` | `useResolveContext` now **takes a shared `idx`** instead of rebuilding the index from the store. `nonce` is part of context identity only — bumping it no longer rebuilds the 73k-entry index. |
| `src/pages/OperationsPage.tsx` | Builds **one** `idx` per model change and feeds it to discovery, scope, and resolution. The `nonce` bump is **debounced** (`REFRESH_DEBOUNCE_MS = 400`) so a burst of events triggers one re-resolution, not one per event. |
| `src/stores/modelStore.test.ts`, `src/hooks/useModelData.test.ts` | Updated for batched/debounced behavior + new regression tests: a burst coalesces into a single store write; `applyBatch` upsert/remove/property-mutate in one write; untouched collections keep identity. |

**Net effect:** per-event work is converted into coalesced work at a bounded
cadence (≤150 ms structural, ≤400 ms re-resolution), the redundant 3× index
rebuild is gone, and a `nonce` bump no longer rebuilds the index. The Operations
page stays responsive at the current sim scale.

### Verifying

- `cd VillageOS-API/vos.Trellis && npx vitest run` → all green.
- `npx tsc -b` clean; `eslint` clean on the changed files.
- Rebuild into the live GUI:
  `VOS_MYCELIUM_WWWROOT=<path-to>/VillageOS/vos.Mycelium/wwwroot npm run build`
  then reload the local Mycelium URL and sign in with your local credentials.
- *Not yet done:* an interactive browser confirmation — the shared Playwright
  browser was locked by another session during this work. Worth a manual pass:
  open Operations, let the sim run for several minutes, confirm it stays smooth.

---

## 3. Remaining work (the "real fixes") — NOT DONE

The quick wins fix *degradation over time* (a CPU problem). They do **not** bound
the browser's **memory** or the **initial-load / refetch** cost: the full model
(25 MB+ and growing) is still fetched on mount, on every `ModelChanged`, and on
SSE reconnect, and still lives in the store. Those are the architectural items.

### 3a. Server-side aggregate endpoint (removes model-size from the hot path)

**Goal.** Let the Operations dashboard's heavy bindings resolve server-side so the
browser no longer needs the whole model to show counts/sums. Generic and
seed-independent (matches the "dashboards are generic; mapping lives in a config
Thing or a service" principle).

**Proposed endpoint (Mycelium, `vos.Mycelium/Controllers/ThingsController.cs`):**
```
POST /api/things/aggregate
{
  "archetype": "<archetype name>",
  "op": "count" | "sum" | "avg" | "min" | "max",
  "property": "<property key>",                   // required unless op == count
  "where":  [ { "property": "...", "op": "=|!=|>|>=|<|<=|in", "value": ... } ],
  "scope":  { "viaPredicate": "...", "direction": "in|out", "entityId": "..." }  // optional
}
-> { "value": <number> }
```

**What it must reproduce from the client resolver** (`src/api/dashboardApi.ts`,
which is the current, well-tested source of truth — mirror it exactly to avoid
divergence):
- **Transitive `is`-archetype membership, instances only** (`thingIdsOfArchetype`):
  descend the `is`-chain from the archetype Thing; a node that is itself an
  `is`-target is a sub-archetype (descend), otherwise it's an instance (count it).
  Cycle-guarded. **No server helper exists for this today — it must be written.**
- **Effective property values.** Use `VosObject.GetPropertiesWithSource()`
  (`vos.Core/VosObject.cs:277`) which resolves own + inherited properties — this
  is the server-side equivalent of the client's `effectiveProperties` merge of
  `Properties` + `InheritedOverrides`.
- **Filters + numeric coercion** (`passesFilters`, `num`) and **scope member
  filtering** (`scopeMemberIds` — members related to `scope.entityId` via a
  predicate in the given direction).

**Frontend rewiring (`resolveBinding` in `src/api/dashboardApi.ts`):** route the
`aggregate` case (and optionally `stateCount`) to the new endpoint instead of
walking the client model. These become async server calls returning a number, so
the Operations page's per-refresh cost is O(widgets) network calls, independent of
model size. The existing `dashboardApi.test.ts` tests resolve bindings against an
in-memory index — they'll need to mock the endpoint for the rewired kinds.

**Cost/risk.** ~1 day of careful C# + tests. Main risk is subtle divergence from
the client resolver (transitive `is` semantics, effective-property merge, `in`
filter, numeric/boolean coercion). Mitigate by porting the TS logic line-for-line
and adding backend tests that mirror the `dashboardApi.test.ts` cases.

**Note on ROI.** After the quick wins, client-side aggregation is already cheap
(once per ~400 ms over ~73k entries). This endpoint's near-term perf gain is
marginal; its real value is enabling 3b (a model-free Operations page) and
correctness at much larger scale.

### 3b. Bound / slim what the browser holds and fetches

**Problem.** `thingApi.getAll()` + `relationshipApi.getAll()` pull the entire model
(no filter, no pagination), it grows unbounded, and it's refetched wholesale on
`ModelChanged` / reconnect. This is the memory + initial-load ceiling for long
runs.

**Options (in rough order of value/effort):**
- **Make the Operations page model-free.** Once 3a lands, the Operations page's
  bindings can all be server-resolved (`aggregate`/`stateCount`/`stateList` are
  already server-backed; `property`/`compareEntities`/`discoverDashboards`/
  `scopeEntities` would need small server endpoints). Then that page doesn't
  touch `modelStore` at all — its cost is independent of model size and it holds
  no model in memory.
- **Server pagination/filtering on `/api/things`** (and relationships) — e.g.
  `?archetype=`, `?limit/offset` or a cursor. `GetThings` currently returns
  everything (`ThingsController.cs:46`).
- **Scoped hydration / eviction.** Only hydrate what the current page needs, or
  evict Things from the store once they leave the active view.

**Blocker to be careful of.** The **Graph** page and the **detail** windows
(`useEntityDetail`) depend on holding the full model client-side (they walk all
relationships). Any model-bounding must be per-page (Operations goes lean; Graph
keeps its full-model load, ideally itself paginated/virtualized later) or those
pages break. This is why 3b is bigger than 3a and wasn't bundled into the quick
wins.

---

## 4. Quick reference — key files

**Frontend (`VillageOS-API/vos.Trellis/src`)**
- `stores/modelStore.ts` — the in-browser model + `applyBatch`.
- `hooks/useModelData.ts` — SSE → store (now buffered/debounced). App-shell hook.
- `hooks/useSse.ts` — SSE plumbing (object subscription + system events).
- `api/dashboardApi.ts` — binding resolver + model index (`buildModelIndex`,
  `thingIdsOfArchetype`, `resolveBinding`). **This is what 3a must mirror.**
- `hooks/useDashboard.ts` — `useResolveContext` / `useBinding`.
- `pages/OperationsPage.tsx` — the dashboard page (shared index + debounced nonce).

**Backend (`VillageOS/vos.Mycelium`, `VillageOS/vos.Core`, `VillageOS/vos.Application`)**
- `Controllers/ThingsController.cs` — where the aggregate endpoint goes.
- `Controllers/RangesController.cs:217` — `GET /states/{name}/things` (pattern to
  mirror; state lookups are already O(1) via a StateIndex).
- `vos.Core/VosObject.cs:277` — `GetPropertiesWithSource()` (effective props).
- `vos.Application/VosThingService.cs:47` — `ToJsonFragment` (the `{Id, Name,
  Properties, InheritedOverrides}` shape the client merges).
