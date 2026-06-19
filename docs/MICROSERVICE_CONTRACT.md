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

One HS256 JWT. The service token (`--token`) carries `vos:token_type=service` + a scope. Send it
as `Authorization: Bearer <jwt>` on every Mycelium call. Inbound `/handle` calls are signed by
Mycelium; validate them against `--signingKey` with the given issuer/audience.

EventSource and other browser/streaming clients that can't set headers pass the JWT as
`?access_token=<jwt>` on the SSE stream URLs instead.

## Handler endpoints (the service exposes)

| Method | Path | Body / result |
|--------|------|---------------|
| `POST` | `/handle` | Mycelium posts a relationship (`relationshipId`, `subjectId`, `targetId`, `properties`); reply `{ "success": true }` |
| `GET` | `/health` | `200` `{ "status": "healthy" }` |
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
`StatesChanged`. Fire-and-forget (no resume); refetch on reconnect.

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
