# simulator

Play a **timeline of model changes** against a live Mycelium, at real time or faster.

A *timeline* is a list of `Action`s in the order of their offsets, the same on every run — each one
change (create a Thing, create a relationship, post a Fact, adjust a quantity, delete a Thing, or
post to a route outside the write API) carrying the `offset`, in simulated seconds, at which it is
due. The simulator waits for each offset (scaled by `--speed`), sends quantity changes through a
`BalanceLedger` so no balance ever goes negative, and posts through the Mycelium client. The platform
stamps every write with the time it commits it, so a post is recorded as happening now, and ranges,
states and the event stream react as they would to a real person or system: the change shows in
Trellis as it happens.

The simulator plays whatever timeline it is handed and knows nothing about any particular model. A
caller builds its own timeline of `Action`s and hands it over, either in its own process or as a file.

## Files

| File | What it is |
| --- | --- |
| `mycelium.py` | A small Mycelium HTTP client written with Python's standard library (Things, relationships, Facts, quantity adjustments, subscriptions and the event stream, and a plain post to any route) plus `stable_id`. |
| `simulator.py` | The player: `Action`, `BalanceLedger`, `Checkpoint`, `Simulator`, `follow_until`, and a command that plays a timeline file. |
| `test_simulator.py` | The ledger never taking a balance below zero when actors run at the same time, resuming from a checkpoint, saving and reading actions, where a credential comes from, and a run against a stand-in Mycelium that raises an error whenever a balance would go below zero. The stand-in covers the write API only — it refuses the subscription and stream calls, which belong against a running Mycelium. |

No packages to install — plain Python 3.10 or later.

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

## Use it from the command line

Save a timeline as one JSON `Action` per line (with `Action.to_dict()` or `write_timeline`) and play it:

```bash
export VOS_TOKEN=<editor or admin token>
python3 simulator.py --url https://localhost:7243 --timeline run.jsonl \
    --speed 120 [--seed-first] [--follow] [--checkpoint run.ckpt] [--dry-run]
```

## Action ops

| op | args | effect |
| --- | --- | --- |
| `apply_fragment` | `things, relationships, name?` | `POST /api/model/fragment` — a batch that adds what is missing and updates what is there, so posting it again changes nothing; how creates are sent (see *Inherited names* below) |
| `create_thing` | `name, thing_id, properties` | `POST /api/things` — used only where a create is not folded into an `apply_fragment` (see below) |
| `create_rel` | `subject_id, predicate_id, target_id`, `predicate?` | `POST /api/relationships` — a relationship made with its subject is folded into the subject's fragment, and a later one stays a separate call |
| `set_fact` | `thing_id, prop, value` | `POST …/facts` (durable, editor/admin) |
| `set_observation` | `thing_id, prop, value, observed_at?` | `POST …/observations` (sampled; the one caller-supplied time) |
| `increment` / `decrement` | `thing_id, prop, amount` | quantity adjust, routed through the `BalanceLedger` |
| `ledger_set` | `thing_id, amount` | record a starting balance (no write) so the first consume is judged against the right quantity |
| `delete_thing` | `thing_id` | `DELETE /api/things/{id}` (retraction) |
| `http_post` | `path, body?, headers?, address?, reads?` | `POST` to a route outside the write API, through the client's request path — a submission to the intake service, for instance. The answer is kept under the action's key for the run; `reads` names, for each `{{name}}` placeholder the action carries, the field of an earlier post's answer to fill it from, so a post can read what the one before answered. Answers are not journaled, so a read of a post committed before a restart is refused rather than posting the placeholder |

## What it gets right

- **Never takes a balance below zero.** `POST …/decrements` refuses a decrement that would go negative
  (HTTP 400 with `available` and `requested`). The `BalanceLedger` holds one lock per balance, keeps
  the quantity itself, and takes at most what is there — a shortfall is a normal outcome, and a 400
  *inside* the lock means the ledger and Mycelium disagree (a lost write, or a run played twice), so
  the run stops with that error.
- **Plays every action it is handed.** Setup sends Thing and relationship creations as one batch —
  the combined fragment, or the `--seed-first` document. Any other setup action (`set_fact`,
  `set_observation`, `increment`, `decrement`, `delete_thing`) is applied on its own straight after
  that batch, so it lands on a Thing that already exists. An action therefore means the same at setup
  as it does on the clock, and none is dropped for being part of the setup.
- **Can be started again safely, with nothing changed on the platform.** Every created Thing is keyed
  to `stable_id(...)` and sent with that `Id`; the `Checkpoint` records how far the run got. A second
  run skips what the first did, and an action repeated after a crash is *refused* (not merged) — on
  purpose, because an unexpected duplicate should fail rather than quietly update a Thing.
  `--from-offset` resumes by simulated time.
- **Signs in the browser's event stream.** A browser's EventSource cannot set headers, so a **stream
  token** goes in `?access_token=` (and the point to resume from in `&lastEventId=`) —
  `client.stream_url(...)` fetches one and builds the address, the way Trellis does. It is the only
  credential the platform accepts there, because an address is recorded where a header is not.
  `follow()` sends the token in a header instead, and fetches nothing.
- **Doesn't fake the clock.** `--speed` changes only *when* each POST is issued, never the stored
  timestamps. The only caller-supplied time is an Observation's optional `observed_at`.
- **Handles inherited names on the server.** A Thing may not hold a property of its own under a name
  it inherits. A timeline writes `create_thing(props)` then `create_rel(is)`, which, sent one call at
  a time, would post the instance's own properties *before* the `is` relationship and be refused by a
  live Mycelium. So the simulator folds each `create_thing` and the `create_rel` actions made with it
  (at the **same offset**, with the new Thing as their subject) into one `apply_fragment`. The server
  then creates the Thing bare, adds the `is` relationship, stores any inherited value as an
  **override**, and records the Facts and events each step would have. Posting the same fragment again
  adds nothing, so no duplicate check is needed. A fragment only adds and updates — it never deletes,
  retracts or renames — so retiring a Thing stays a separate `delete_thing`. The server checks a
  fragment **before** applying it (references, typed values, composed names), so a malformed one fails
  with `400` and changes nothing, and a failure while it is being applied is undone before the `400`.
  Properties travel as typed values, so decimals are not cut to whole numbers. The **setup**'s
  creations become one fragment (`ledger_set` actions still come before it, and every other setup
  action after it); `--seed-first` instead loads the separate creations through `POST /api/model` and
  applies the rest one at a time. An `is` relationship at a later offset than its Thing is made to a
  Thing that already exists and stays a separate `create_rel`. The folding reads only the sorted
  timeline, so checkpoint indices and pacing are unchanged: a fragment sits at its `create_thing`'s
  offset. It is part of the simulator itself, so every timeline gets it.

## Credentials

Structural writes (`/things`, `/relationships`, `/facts`, `/increments`) need `ModifyData` /
`WritePropertyFact`, both of which admit editor, admin, and service roles.

**Neither credential is an argument.** A command line is readable by every process on the host and is
kept in the shell's history file, and a replay runs for as long as its timeline lasts — so the client
reads both from the environment, and `--token` or `--api-key` is refused as an unknown argument.

| Variable | Meaning |
| --- | --- |
| `VOS_TOKEN` | An editor or admin token, used as it is. |
| `VOS_API_KEY` | An API key a token is fetched with: `POST /api/auth/token` with the key in the **`X-API-Key`** header (add `--model-id` for a host holding several models, sent as `?modelId=`), which answers `{token}`. |

With neither set, the first request fails saying so. A caller in the same process may still hand a
credential straight to `MyceliumClient(...)` — a library call is not a command line — and that wins
over the environment. The token is sent only to the client's own address: an `http_post` naming
another `address` is sent without it.
