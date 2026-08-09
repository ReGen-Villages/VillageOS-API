# simulator

Replay a **timeline of graph changes** against a live Mycelium in (accelerated) real time.

A *timeline* is a deterministic, offset-ordered list of `Action`s — each one intent (create a Thing,
create a Relationship, post a Fact, adjust a quantity, delete a Thing) carrying the simulated-second
`offset` at which it comes due. The simulator sleeps to each offset (scaled by `--speed`), routes
quantity deltas through a `BalanceLedger` so no balance is ever driven negative, and POSTs via the
Mycelium client. Because the platform stamps every write `CommittedAt = UtcNow` (no backdating), a
POST *now* is genuinely now and flows reactor → range re-evaluation → derived-status change → SSE:
the change animates in Trellis as if a real user or external system had acted.

The simulator is **domain-agnostic**. It plays whatever timeline it is handed and knows nothing about
any particular model. A domain produces its own timeline of `Action`s and hands it over — either as a
library or as a serialized file.

## Files

| File | What it is |
| --- | --- |
| `mycelium.py` | A minimal, standard-library Mycelium HTTP client (Things, Relationships, Facts, quantity adjustments, subscriptions/SSE) plus `stable_id`. |
| `simulator.py` | The driver: `Action`, `BalanceLedger`, `Checkpoint`, `Simulator`, `follow_until`, and a CLI that plays a timeline file. |
| `test_simulator.py` | Ledger no-oversell under concurrency, checkpoint resume, action serialization, dry replay against a fake Mycelium that fails loud on any oversell. That double stands in for the write API only — it refuses the subscription and stream calls, which belong against a running Mycelium. |

No third-party dependencies — stock Python 3.10+.

## Use it as a library

```python
from mycelium import MyceliumClient, stable_id
from simulator import Action, Simulator

actions = [
    Action(offset=0, seq=0, actor="setup", op="create_thing",
           args={"name": "bin-A", "thing_id": stable_id("bin", "A"),
                 "properties": {"contained_units": 100}}, key="bin-A"),
    Action(offset=0, seq=1, actor="setup", op="ledger_set",
           args={"thing_id": stable_id("bin", "A"), "amount": 100}, key="bin-A"),
    Action(offset=30, seq=2, actor="worker", op="decrement",
           args={"thing_id": stable_id("bin", "A"), "prop": "contained_units", "amount": 5}, key="draw"),
]

client = MyceliumClient("http://localhost:5000", token="<editor-jwt>")
Simulator(client, speed=120, checkpoint="run.ckpt").run(actions)
```

## Use it as a CLI

Serialize a timeline to JSONL (one `Action` per line via `Action.to_dict()` / `write_timeline`) and play it:

```bash
python3 simulator.py --url http://localhost:5000 --token "$JWT" --timeline run.jsonl \
    --speed 120 [--seed-first] [--follow] [--checkpoint run.ckpt] [--dry-run]
```

## Action ops

| op | args | effect |
| --- | --- | --- |
| `apply_fragment` | `things, relationships, name?` | `POST /api/model/fragment` — an upsert/idempotent partial-model batch; the **main path** for creates (see *Lazy inheritance* below) |
| `create_thing` | `name, thing_id, properties` | `POST /api/things` — **fallback**; the coalescing pass folds creates into `apply_fragment` (see below) |
| `create_rel` | `subject_id, predicate_id, target_id`, `predicate?` | `POST /api/relationships` — **fallback**; a creation-time edge is folded into its subject's fragment, a later lifecycle edge stays granular |
| `set_fact` | `thing_id, prop, value` | `POST …/facts` (durable, editor/admin) |
| `set_observation` | `thing_id, prop, value, observed_at?` | `POST …/observations` (sampled; the one caller-supplied time) |
| `increment` / `decrement` | `thing_id, prop, amount` | quantity adjust, routed through the `BalanceLedger` |
| `ledger_set` | `thing_id, amount` | seed a starting balance (no write) so the first consume resolves absolute |
| `delete_thing` | `thing_id` | `DELETE /api/things/{id}` (retraction) |

## What it gets right

- **Never oversells.** `POST …/decrements` rejects a decrement that would go negative (HTTP 400 with
  `available`/`requested`). The `BalanceLedger` holds one lock per balance, resolves to an absolute
  quantity, and applies at most what is available — a shortfall is a legitimate outcome, and a 400
  *inside* the lock means a real divergence (a lost write, a double-run) and fails loud.
- **Idempotent on resume, no platform change.** Every created Thing is keyed to `stable_id(...)` and
  passed as a client-supplied `Id`; the `Checkpoint` journals the committed prefix. A re-run skips
  what it already did, and a straggler duplicate is *rejected* (not merged) — which is intended: a
  surprise duplicate should fail, not silently upsert. `--from-offset` resumes by simulated time.
- **Authenticates the browser stream.** EventSource can't set headers, so the JWT rides as
  `?access_token=` (resume `&lastEventId=`) — `client.stream_url(...)` builds it, the way Trellis does.
- **Doesn't fake the clock.** `--speed` changes only *when* each POST is issued, never the stored
  timestamps. The only caller-supplied time is an Observation's optional `observed_at`.
- **Respects lazy inheritance (I1), resolved server-side.** Under lazy inheritance a Thing may not
  *own* a property name it *inherits*. Timelines emit the natural domain shape — `create_thing(props)`
  then `create_rel(is)` — which, sent granularly, would post the instance's own properties *before*
  the `is` edge and make the edge fail with I1 on a live Mycelium. The simulator no longer choreographs
  a client-side workaround (bare-create then override). Instead a deterministic **coalescing pass**
  folds each `create_thing` and its *creation-time* edges (a `create_rel` at the **same offset** whose
  subject **is** the new Thing) into a single `apply_fragment`, and `POST /api/model/fragment` upserts
  the batch: the **server** creates the Thing bare, establishes the `is` edge, materializes any
  inherited value as an **override**, and emits the granular SSE/Facts. The endpoint is
  upsert/idempotent, so a re-post never duplicates and needs no duplicate-guard. The upsert is
  **additive-only** — it creates and updates but never deletes/retracts or renames, so a fragment can
  only grow or update the model; retiring a Thing stays a granular `delete_thing`. Validation is
  **up front** (references + typed envelopes), so a malformed fragment fails `400` with zero mutation;
  application is not yet transactional, but because it is idempotent a partially-applied batch self-heals
  on the next post. Properties travel as typed envelopes, so decimals/measures don't truncate. The whole **setup** (standing world) collapses
  into one bulk fragment (`ledger_set` actions still precede it); `--seed-first` instead bulk-loads the
  granular setup via `POST /api/model`. A later-offset `is` edge on an already-existing Thing is a
  lifecycle edge and stays a granular `create_rel`. Coalescing is a pure function of the sorted
  timeline, so checkpoint indices and pacing are unchanged — a paced fragment sits at its
  `create_thing`'s offset and fires at that instance's moment. This lives in the generic engine, so
  every domain timeline gets it for free.

## Token

Structural writes (`/things`, `/relationships`, `/facts`, `/increments`) need `ModifyData` /
`WritePropertyFact`, both of which admit editor, admin, and service roles. Pass an editor/admin JWT via `--token`, or
an API key via `--api-key` to mint one: the client calls `POST /api/auth/token` with the key in the
**`X-API-Key`** header (add `--model-id` for a multi-model host → `?modelId=`), which returns `{token}`.
