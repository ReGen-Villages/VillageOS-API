# VillageOS Microservice Contract (HTTP + SSE)

The contract every managed microservice speaks, independent of language. It is plain HTTP plus
a single HS256 JWT — implementable in any stack. The Go, Node, Python, and Rust services in this
repo are reference implementations of the handler side; the shared .NET `SubscriptionClient` and
Trellis are reference consumers of the subscription side.

A service plays one or both roles:

- **Handler** — exposes `POST /handle`, `GET /health`, `POST /shutdown`; registers with Mycelium
  and is invoked per matching relationship. (All four reference services do this.)
- **Subscriber** — needs live model data, so it takes a snapshot and follows an SSE stream
  instead of polling. (Metabolism does this; the snippets below show it in each language.)

A handler may additionally act as a **pipeline DAG node** by recognising one extra `/handle` request
shape (the node envelope) and replying with outputs — see the
[MICROSERVICES.md §16.2 (node contract)](MICROSERVICES.md). It is purely additive to everything below.

## Startup

Mycelium launches a daemon with these flags (a service ignores ones it doesn't need):

| Flag | Meaning |
|------|---------|
| `--port` | Port the service listens on |
| `--myceliumUrl` | Mycelium base URL |
| `--token` | Pre-minted service JWT (else fetch from `POST /api/auth/token`) |
| `--signingKey` | Base64 HMAC key for validating inbound `/handle` JWTs |
| `--issuer` / `--audience` | Expected JWT issuer/audience (validation must match what Mycelium signs) |

Plain service-specific flags (e.g. `--mode=consumes`) are passed through verbatim.

## Auth

One HS256 JWT. The service token (`--token`) carries `vos:token_type=service` + a scope; use it for
the daemon's own registration/deregistration. Inbound `/handle` calls are signed by Mycelium with a
short-lived service token carrying the request's `vos:model_id`; validate them against `--signingKey`
with the given issuer/audience, and reuse the inbound token for any callback so a shared daemon acts
on the request's model.

EventSource and other browser/streaming clients that can't set headers pass the JWT as
`?access_token=<jwt>` on the SSE stream URLs instead.

## Handler endpoints (the service exposes)

| Method | Path | Body / result |
|--------|------|---------------|
| `POST` | `/handle` | Mycelium posts a relationship (`relationshipId`, `subjectId`, `targetId`, `properties`); reply `{ "success": true }` |
| `GET` | `/health` | `200` `{ "status": "Healthy" }` |
| `POST` | `/shutdown` | Begin graceful shutdown (stop work, deregister) |

## Registration

- `POST /api/mycelium/register` (Bearer) — `{ handlerId, serviceName, endpointUrl, startCommand, stopEndpoint, healthEndpoint }`
- `DELETE /api/mycelium/services/{handlerId}` (Bearer) — on shutdown

An open SSE subscription also counts as a liveness signal: a service that is actively streaming
is treated as healthy even if its `/health` is briefly unreachable.

## Subscriptions (live model data over SSE)

Replaces GET-storm polling: take one snapshot, then follow changes.

### `POST /api/subscriptions` (Bearer) → snapshot + watermark

Body is a selector:

```jsonc
{
  "all": false,                       // true = whole model + all future objects
  "ids":   ["<guid>"],                // seed objects
  "names": ["Pump-01"],               // seed by name
  "types": ["Pump"],                  // seed by type (transitive `is`)
  "traverse": [ { "predicate": "produces", "direction": "outgoing", "depth": 1 } ],
  "includeIsAncestors": true,         // default true (keeps inherited values correct)
  "includeRelationships": true
}
```

Returns `{ subscriptionId, watermark, snapshot }`. The snapshot lists `things` and
`relationships`, each with own `Properties` and `InheritedProperties` (kept separate), `States`,
and incident relationship ids. `watermark` is the commit sequence the snapshot was taken at.

### Selecting a slice (the startup-template replacement)

The selector is how a handler says *which* objects it wants — it replaced the retired `ServiceArgs`
ID template. Instead of Mycelium injecting object IDs into your launch command, you ask for
the slice **by shape** and get exactly that closure. Recipes:

| Need | Selector body |
|---|---|
| Whole model + all future objects | `{ "all": true }` |
| Specific objects by id | `{ "ids": ["<guid>", …] }` |
| By name | `{ "names": ["Pump-01"] }` |
| Every Thing of a type (transitive `is`) | `{ "types": ["Pump"] }` |
| A type **and** its neighbours along an edge | `{ "types": ["Battery"], "traverse": [{ "predicate": "powers", "direction": "outgoing", "depth": 1 }] }` |
| Drop inherited type-default values | add `"includeIsAncestors": false` |
| Things only, no relationships | add `"includeRelationships": false` |

`traverse.direction` is `outgoing` \| `incoming` \| `both`; `depth` walks N hops. Fields combine —
the closure is the union of all seeds, then the traversal and `is`-ancestor expansion. Every
reference handler ships a runnable example at `POST /demo/subscribe { "type": "Battery", "predicate":
"powers" }` that subscribes for that slice, reports the resolved closure (counts + names), and
unsubscribes.

#### Reference: select a slice, per language

```csharp
// C# (.NET) — SubscriptionClient
var sel = new SubscriptionSelector { Types = new() { "Battery" },
    Traverse = new() { new() { Predicate = "powers", Direction = "outgoing", Depth = 1 } } };
SubscribeResult sub = await subscriptions.SubscribeAsync(sel);
// sub.Snapshot.Things / .Relationships is exactly the requested closure; then follow StreamAsync.
```

```go
sub, _ := s.subscribe(sliceByTypeAndTraverse("Battery", "powers"))
// sub.Snapshot.Things / sub.Snapshot.Relationships = the requested closure
```

```ts
const sub = await subscribe(cfg, sliceByTypeAndTraverse("Battery", "powers"));
```

```python
sub = await subscribe(slice_by_type_and_traverse("Battery", "powers"))
```

```rust
let sub = subscribe(cfg, &http, &slice_by_type_and_traverse("Battery", "powers")).await?;
```

### `GET /api/subscriptions/{id}/stream` → SSE change stream

`Accept: text/event-stream`. Each event:

```text
id: 1235
event: PropertyChanged
data: {"Kind":"PropertyChanged","EntityId":"<guid>","PropertyName":"temp","Value":92}

```

`id:` is the commit sequence. Kinds: `ThingCreated`, `ThingDeleted`, `RelationshipCreated`,
`RelationshipDeleted`, `PropertyChanged`, `PropertyDeleted`, `RelationshipPropertyChanged`.
**Resume:** on reconnect send the last sequence seen via the `Last-Event-ID` header (EventSource
does this automatically) or `?lastEventId=`; the server replays committed changes after it, then
goes live — gap-free and exactly-once. Initial connect resumes from the snapshot `watermark`.

### Mutable membership (no reconnect)

- `POST /api/subscriptions/{id}/objects` (selector body) — add objects; returns an incremental snapshot of the added closure
- `DELETE /api/subscriptions/{id}/objects` (`{ "ids": [...] }`) — drop objects
- `DELETE /api/subscriptions/{id}` — unsubscribe

### `GET /api/events/stream` → operational events

A separate stream for non-object events: `ActivityEvent`, `ModelChanged`, `ModelCleared`,
`ServiceHealthChanged`, `DaemonStatusChanged`, `EndpointServiceRequestCompleted`,
`ServiceRequestCompleted`, `StatesChanged`. Fire-and-forget (no resume); refetch on reconnect.

> Field casing: snapshot JSON is camelCase; SSE `data` payloads are PascalCase. Parse
> case-insensitively (all reference clients do).

## Reference: subscribe + follow, per language

Each snippet POSTs an all-model subscription, opens the stream, and applies changes; reconnect +
`Last-Event-ID` resume is shown where the language's SSE client doesn't do it natively.

### Go

```go
// POST /api/subscriptions {all:true} -> {subscriptionId, watermark}; then read the SSE stream.
body, _ := json.Marshal(map[string]any{"all": true})
req, _ := http.NewRequest("POST", base+"/api/subscriptions", bytes.NewReader(body))
req.Header.Set("Authorization", "Bearer "+token)
req.Header.Set("Content-Type", "application/json")
resp, _ := client.Do(req)
var sub struct{ SubscriptionID string `json:"subscriptionId"`; Watermark int64 `json:"watermark"` }
json.NewDecoder(resp.Body).Decode(&sub)

last := sub.Watermark
for { // reconnect loop; EventSource-style resume via Last-Event-ID
    sreq, _ := http.NewRequest("GET", fmt.Sprintf("%s/api/subscriptions/%s/stream", base, sub.SubscriptionID), nil)
    sreq.Header.Set("Authorization", "Bearer "+token)
    sreq.Header.Set("Last-Event-ID", strconv.FormatInt(last, 10))
    sresp, err := client.Do(sreq)
    if err != nil { time.Sleep(2 * time.Second); continue }
    sc := bufio.NewScanner(sresp.Body)
    var id, kind, data string
    for sc.Scan() {
        line := sc.Text()
        switch {
        case strings.HasPrefix(line, "id:"):    id = strings.TrimSpace(line[3:])
        case strings.HasPrefix(line, "event:"): kind = strings.TrimSpace(line[6:])
        case strings.HasPrefix(line, "data:"):  data = strings.TrimSpace(line[5:])
        case line == "":
            if data != "" { apply(kind, data); if id != "" { last, _ = strconv.ParseInt(id, 10, 64) } }
            id, kind, data = "", "", ""
        }
    }
    sresp.Body.Close()
}
```

### Node

```js
import { EventSource } from 'eventsource'; // sets Last-Event-ID on reconnect automatically

const sub = await (await fetch(`${base}/api/subscriptions`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
  body: JSON.stringify({ all: true }),
})).json();

const es = new EventSource(
  `${base}/api/subscriptions/${sub.subscriptionId}/stream?lastEventId=${sub.watermark}`,
  { fetch: (u, o) => fetch(u, { ...o, headers: { ...o.headers, Authorization: `Bearer ${token}` } }) },
);
for (const kind of ['ThingCreated','ThingDeleted','PropertyChanged','RelationshipPropertyChanged'])
  es.addEventListener(kind, e => apply(kind, JSON.parse(e.data)));
```

### Python

```python
import json, httpx, time

sub = httpx.post(f"{base}/api/subscriptions", json={"all": True},
                 headers={"Authorization": f"Bearer {token}"}).json()
last = sub["watermark"]
while True:  # reconnect loop with Last-Event-ID resume
    headers = {"Authorization": f"Bearer {token}", "Accept": "text/event-stream",
               "Last-Event-ID": str(last)}
    try:
        with httpx.stream("GET", f"{base}/api/subscriptions/{sub['subscriptionId']}/stream",
                          headers=headers, timeout=None) as r:
            ev, data = None, None
            for line in r.iter_lines():
                if line.startswith("id:"):     last = int(line[3:].strip())
                elif line.startswith("event:"): ev = line[6:].strip()
                elif line.startswith("data:"):  data = line[5:].strip()
                elif line == "" and data:       apply(ev, json.loads(data)); ev = data = None
    except httpx.HTTPError:
        time.sleep(2)
```

### Rust

```rust
// reqwest streaming body; parse SSE frames; reconnect with Last-Event-ID.
let sub: serde_json::Value = client.post(format!("{base}/api/subscriptions"))
    .bearer_auth(&token).json(&serde_json::json!({ "all": true })).send().await?.json().await?;
let id = sub["subscriptionId"].as_str().unwrap().to_string();
let mut last = sub["watermark"].as_i64().unwrap();
loop {
    let resp = client.get(format!("{base}/api/subscriptions/{id}/stream"))
        .bearer_auth(&token).header("Last-Event-ID", last.to_string()).send().await;
    let Ok(resp) = resp else { tokio::time::sleep(Duration::from_secs(2)).await; continue };
    let mut stream = resp.bytes_stream();
    let (mut ev, mut data) = (String::new(), String::new());
    // accumulate lines from the byte stream; on a blank line dispatch (ev, data)
    // and update `last` from the most recent `id:` field, then keep reading.
    while let Some(Ok(chunk)) = stream.next().await { /* parse id:/event:/data:/blank per SSE */ }
}
```

## Writing data back: Facts, Observations, Sediment

A handler often needs to write to the model, not just read it. There are three write kinds, each a
Bearer-authed POST. Pick by intent:

| Kind | When | Route | Body | Success |
|---|---|---|---|---|
| **Fact** | structural truth that must survive replay (status, config, a corrected value) — synchronous, never lossy | `POST /api/things/{id}/properties/{property}/facts` | `{ "value": <scalar> }` | `201 { sequenceNumber, value }` |
| **Observation** (single) | one sampled telemetry value — queued & batched | `POST /api/things/{id}/properties/{property}/observations` | `{ "value": <scalar>, "observedAt"?: <iso8601> }` | `202` |
| **Observation** (batch) | many samples across one entity's properties, one call | `POST /api/things/{id}/observations` | `[{ "property", "value", "observedAt"? }]` | `202 { accepted }` |
| **Sediment** | bulk historical load written straight to sealed Sapwood; entities must already exist; `observedAt` **required** | `POST /api/sediment` | `[{ "thingId", "property", "value", "observedAt" }]` | `202 { batchId, series, buckets, samples }` |

**Gating.** A property declares which kinds it accepts (`AllowedWriteKinds`: `Both` / `FactOnly` /
`ObservationOnly`). Writing the wrong kind is rejected with **405** — a Fact to an `ObservationOnly`
property, or an observation to a `FactOnly` one. An unknown thing/property is **404**.

**Runnable demo.** Every reference handler exposes `POST /demo/write-kinds { "thingId": "<existing>" }`,
which performs one of each kind against a Thing whose `status` accepts Facts and `temperature`/`flow`
accept Observations.

## Writing structure back: the fragment upsert

The writes above set property **values**. To create or update **structure** — Things, their
relationships (including the `is` type edge), and their initial values — in one call, POST a
**fragment**: a partial-model `{ "Things", "Relationships" }` batch.

| Route | Body | Success |
|---|---|---|
| `POST /api/model/fragment` | `{ "Name", "Things": [ {Id, Name, Properties} ], "Relationships": [ {Name, Subject, Predicate, Target} ] }` | `200 { thingsCreated, thingsUpdated, relationshipsCreated, things }` |

- **Upsert, idempotent.** Existing Things/edges are left in place (values re-applied); re-posting the
  same fragment neither duplicates nor errors. `ModifyData` (editor/admin/**service**).
- **Additive-only.** A fragment only *creates or updates*. It never deletes or retracts Things, edges,
  or properties, and never renames an existing Thing (identity is by `Id`; the `Name` sent for a known
  `Id` is ignored). Anything in the model but absent from the fragment is left untouched — removing
  structure is a separate, explicit operation.
- **Validate-first, not yet transactional.** Relationship references and typed envelopes are validated
  up front, so a malformed fragment fails `400` with **zero** mutation. Application itself is not yet
  transactional: if a write fails partway through the batch, already-applied changes are **not** rolled
  back (partial application). Because re-posting is idempotent, a retry self-heals. Full transactional
  rollback is a tracked follow-up.
- **Server resolves lazy inheritance (I1).** A Thing that carries a value for a name it will *inherit*
  is created **bare**, gains its `is` edge, then has the value written as an **override** — so you send
  the natural `{Thing-with-own-Properties} + {Thing is Archetype}` shape and never trip I1 yourself.
  Batches order writes so an `is`-target's own properties land before the subject that inherits them.
- **Emits the same Facts/SSE** as the per-write endpoints (it goes through the same fact pipeline), so
  every created Thing/edge/value animates and survives replay. Property values carry a typed envelope
  (`{ "typeInfo": "vos.Decimal", "value": 2.5 }`) so decimals/measures don't truncate.
- **Batch-scale reactive work.** Each write still re-evaluates its own affected ranges, but the O(model)
  roll-up recompute is **deferred and run once** for the whole batch (not per write), so applying a large
  fragment is ~O(model), not O((things + rels) × model). Send big graphs as one fragment rather than many
  single writes: a standing-world batch that would otherwise be quadratic completes in one recompute pass.

Contrast `POST /api/model`, which **replaces** the whole model (admin-only, bulk load); the fragment
endpoint merges incrementally into the live model.

### Reference: write the three kinds, per language

#### C# (.NET) — `MyceliumClientBase` helpers

```csharp
long seq = await mycelium.SetFactAsync(thingId, "status", "active");                 // Fact → 201
await mycelium.RecordObservationAsync(thingId, "temperature", 21.5m, DateTime.UtcNow); // Observation → 202
int n = await mycelium.RecordObservationsAsync(thingId, new[] {                       // batch → 202
    new ObservationSample("temperature", 21.7m),
    new ObservationSample("flow", 3.1m),
});
SedimentDepositResult d = await mycelium.DepositSedimentAsync(new[] {                 // Sediment → 202
    new SedimentReading(thingId, "temperature", 19.8m, DateTime.UtcNow.AddDays(-1)),
});
```

#### Go

```go
seq, _ := s.setFact(thingID, "status", "active")                                   // Fact
_ = s.recordObservation(thingID, "temperature", 21.5, time.Now().UTC().Format(time.RFC3339))
n, _ := s.recordObservations(thingID, []observationSample{{Property: "temperature", Value: 21.7}})
res, _ := s.depositSediment([]sedimentReading{{ThingID: thingID, Property: "temperature",
    Value: 19.8, ObservedAt: "2026-06-19T12:00:00Z"}})
```

#### Node / TypeScript

```ts
const seq = await setFact(cfg, thingId, "status", "active");
await recordObservation(cfg, thingId, "temperature", 21.5, new Date().toISOString());
const n = await recordObservations(cfg, thingId, [{ property: "temperature", value: 21.7 }]);
const res = await depositSediment(cfg, [
  { thingId, property: "temperature", value: 19.8, observedAt: "2026-06-19T12:00:00Z" },
]);
```

#### Python

```python
seq = await set_fact(thing_id, "status", "active")
await record_observation(thing_id, "temperature", 21.5, datetime.now(timezone.utc).isoformat())
n = await record_observations(thing_id, [{"property": "temperature", "value": 21.7}])
res = await deposit_sediment([{"thingId": thing_id, "property": "temperature",
    "value": 19.8, "observedAt": "2026-06-19T12:00:00Z"}])
```

#### Rust

```rust
let seq = set_fact(cfg, &http, thing_id, "status", json!("active")).await?;
record_observation(cfg, &http, thing_id, "temperature", json!(21.5), Some("2026-06-20T12:00:00Z")).await?;
let n = record_observations(cfg, &http, thing_id,
    &[ObservationSample { property: "temperature".into(), value: json!(21.7), observed_at: None }]).await?;
let res = deposit_sediment(cfg, &http,
    &[SedimentReading { thing_id: thing_id.into(), property: "temperature".into(),
        value: json!(19.8), observed_at: "2026-06-19T12:00:00Z".into() }]).await?;
```
