# simulator

Replay a **timeline of graph changes** against a live Mycelium in (accelerated) real time.

A *timeline* is a deterministic, offset-ordered list of `Action`s — each one intent (create a Thing,
create a Relationship, post a Fact, adjust a quantity, delete a Thing, or post to a route outside the
write API) carrying the simulated-second `offset` at which it comes due. The simulator sleeps to each
offset (scaled by `--speed`), routes quantity deltas through a `BalanceLedger` so no balance is ever
driven negative, and POSTs via the Mycelium client. Because the platform stamps every write itself
when it commits (no backdating), a POST *now* is genuinely now and flows reactor → range
re-evaluation → derived-status change → SSE:
the change animates in Trellis as if a real user or external system had acted.

The simulator is **domain-agnostic**. It plays whatever timeline it is handed and knows nothing about
any particular model. A domain produces its own timeline of `Action`s and hands it over — either as a
library or as a serialized file.

## Files

| File | What it is |
| --- | --- |
| `mycelium.py` | A minimal, standard-library Mycelium HTTP client (Things, Relationships, Facts, quantity adjustments, subscriptions/SSE, and a plain post to any route) plus `stable_id`. |
| `simulator.py` | The driver: `Action`, `BalanceLedger`, `Checkpoint`, `Simulator`, `follow_until`, and a CLI that plays a timeline file. |
| `test_simulator.py` | The ledger never driving a balance negative under concurrency, checkpoint resume, action serialization, where a credential comes from, dry replay against a fake Mycelium that fails loud on any balance driven negative. That double stands in for the write API only — it refuses the subscription and stream calls, which belong against a running Mycelium. |

No third-party dependencies — stock Python 3.10+.

## Use it as a library

```python
from mycelium import MyceliumClient, stable_id
from simulator import Action, Simulator

actions = [
    Action(offset=0, seq=0, actor="setup", op="create_thing",
           args={"name": "Reservoir-A", "thing_id": stable_id("reservoir", "A"),
                 "properties": {"storedM3": 100}}, key="Reservoir-A"),
    Action(offset=0, seq=1, actor="setup", op="ledger_set",
           args={"thing_id": stable_id("reservoir", "A"), "amount": 100}, key="Reservoir-A"),
    Action(offset=30, seq=2, actor="household", op="decrement",
           args={"thing_id": stable_id("reservoir", "A"), "prop": "storedM3", "amount": 5}, key="draw"),
]

client = MyceliumClient("https://localhost:7243")    # credential from VOS_TOKEN or VOS_API_KEY
Simulator(client, speed=120, checkpoint="run.ckpt").run(actions)
```

## Use it as a CLI

Serialize a timeline to JSONL (one `Action` per line via `Action.to_dict()` / `write_timeline`) and play it:

```bash
export VOS_TOKEN=<editor/admin JWT>
python3 simulator.py --url https://localhost:7243 --timeline run.jsonl \
    --speed 120 [--seed-first] [--follow] [--checkpoint run.ckpt] [--dry-run]
```

## Action ops

| op | args | effect |
| --- | --- | --- |
| `apply_fragment` | `things, relationships, name?` | `POST /api/model/fragment` — an upsert/idempotent partial-model batch; the **main path** for creates (see *Lazy inheritance* below) |
| `create_thing` | `name, thing_id, properties` | `POST /api/things` — **fallback**; the coalescing pass folds creates into `apply_fragment` (see below) |
| `create_rel` | `subject_id, predicate_id, target_id`, `predicate?` | `POST /api/relationships` — **fallback**; a creation-time relationship is folded into its subject's fragment, a later lifecycle relationship stays granular |
| `set_fact` | `thing_id, prop, value` | `POST …/facts` (durable, editor/admin) |
| `set_observation` | `thing_id, prop, value, observed_at?` | `POST …/observations` (sampled; the one caller-supplied time) |
| `increment` / `decrement` | `thing_id, prop, amount` | quantity adjust, routed through the `BalanceLedger` |
| `ledger_set` | `thing_id, amount` | seed a starting balance (no write) so the first consume resolves absolute |
| `delete_thing` | `thing_id` | `DELETE /api/things/{id}` (retraction) |
| `http_post` | `path, body?, headers?, address?, reads?` | `POST` to a route outside the write API, through the client's request path — a submission to the intake service, for instance. The answer is kept under the action's key for the run; `reads` names, for each `{{name}}` placeholder the action carries, the field of an earlier post's answer to fill it from, so a post can read what the one before answered. Answers are not journaled, so a read of a post committed before a restart is refused rather than posting the placeholder |

## What it gets right

- **Never drives a balance negative.** `POST …/decrements` rejects a decrement that would go negative (HTTP 400 with
  `available`/`requested`). The `BalanceLedger` holds one lock per balance, resolves to an absolute
  quantity, and applies at most what is available — a shortfall is a legitimate outcome, and a 400
  *inside* the lock means a real divergence (a lost write, a double-run) and fails loud.
- **Plays every action it is handed.** Setup folds Thing and Relationship creations into one batch —
  the coalesced fragment, or the `--seed-first` document. Any other setup action (`set_fact`,
  `set_observation`, `increment`, `decrement`, `delete_thing`) is applied on its own straight after
  that batch, so it lands on a Thing that already exists. Both halves of a timeline therefore accept
  the whole op vocabulary: an action means the same thing at setup as it does on the clock, and no
  action is dropped for sitting in the standing world.
- **Idempotent on resume, no platform change.** Every created Thing is keyed to `stable_id(...)` and
  passed as a client-supplied `Id`; the `Checkpoint` journals the committed prefix. A re-run skips
  what it already did, and a straggler duplicate is *rejected* (not merged) — which is intended: a
  surprise duplicate should fail, not silently upsert. `--from-offset` resumes by simulated time.
- **Authenticates the browser stream.** EventSource can't set headers, so a **stream token** rides as
  `?access_token=` (resume `&lastEventId=`) — `client.stream_url(...)` mints one and builds the
  address, the way Trellis does. It is the only credential the platform admits there, because an
  address is recorded where a header is not. `follow()` sends the bearer header instead, and mints
  nothing.
- **Doesn't fake the clock.** `--speed` changes only *when* each POST is issued, never the stored
  timestamps. The only caller-supplied time is an Observation's optional `observed_at`.
- **Respects lazy inheritance, resolved server-side.** A Thing may not *own* a property name it
  *inherits*. Timelines emit the natural domain shape — `create_thing(props)` then `create_rel(is)`
  — which, sent one call at a time, would post the instance's own properties *before* the `is`
  relationship and be refused by a live Mycelium. The simulator works around nothing on the client:
  a deterministic **coalescing pass** folds each `create_thing` and its *creation-time* relationships (a `create_rel` at the **same offset** whose
  subject **is** the new Thing) into a single `apply_fragment`, and `POST /api/model/fragment` upserts
  the batch: the **server** creates the Thing bare, establishes the `is` relationship, materializes any
  inherited value as an **override**, and emits the granular SSE/Facts. The endpoint is
  upsert/idempotent, so a re-post never duplicates and needs no duplicate-guard. The upsert is
  **additive-only** — it creates and updates but never deletes/retracts or renames, so a fragment can
  only grow or update the model; retiring a Thing stays a granular `delete_thing`. Validation is
  **up front** (references, typed envelopes, computed names), so a malformed fragment fails `400` with
  zero mutation, and a failure while the batch is being applied is undone before the `400` — the model
  is never left half-built. Re-posting stays idempotent either way. Properties travel as typed
  envelopes, so decimals/measures don't truncate. The **setup**'s creations (standing world) collapse
  into one bulk fragment (`ledger_set` actions still precede it, and every other setup action follows
  it); `--seed-first` instead bulk-loads the granular creations via `POST /api/model` and applies the
  rest one at a time. A later-offset `is` relationship on an already-existing Thing is a lifecycle
  relationship and stays a granular `create_rel`. Coalescing is a pure function of the sorted
  timeline, so checkpoint indices and pacing are unchanged — a paced fragment sits at its
  `create_thing`'s offset and fires at that instance's moment. This lives in the generic engine, so
  every domain timeline gets it for free.

## Credentials

Structural writes (`/things`, `/relationships`, `/facts`, `/increments`) need `ModifyData` /
`WritePropertyFact`, both of which admit editor, admin, and service roles.

**Neither credential is an argument.** A command line is readable by every process on the host and is
kept in the shell's history file, and a replay runs for as long as its timeline lasts — so the client
reads both from the environment, and `--token` or `--api-key` is refused as an unknown argument.

| Variable | Meaning |
| --- | --- |
| `VOS_TOKEN` | A ready editor/admin JWT. Used as it stands, with no mint. |
| `VOS_API_KEY` | An API key a token is minted from: `POST /api/auth/token` with the key in the **`X-API-Key`** header (add `--model-id` for a multi-model host → `?modelId=`), which returns `{token}`. |

With neither set, the first request fails saying so. In-process callers may still hand a credential
straight to `MyceliumClient(...)` — a library call is not a command line — and that wins over the
environment. The bearer rides only to the client's own address: an `http_post` naming another
`address` is sent without it.
