#!/usr/bin/env python3
"""simulator — replay a timeline of graph changes against a live Mycelium in (accelerated) real time.

A *timeline* is a deterministic, offset-ordered list of `Action`s: each is one intent — create a
Thing, create a Relationship, post a Fact, adjust a quantity, delete a Thing — carrying the
simulated-second `offset` at which it comes due. The simulator sleeps to each offset (scaled by
`--speed`), routes quantity deltas through a `BalanceLedger` so no balance is ever driven negative,
and POSTs via the Mycelium client. The platform stamps every write `CommittedAt = UtcNow` (no
backdating), so anything POSTed now is genuinely now and flows reactor → range re-eval → status
change → SSE — the change animates in Trellis as if a real user or external system had acted.

This is domain-agnostic: it plays whatever timeline it is handed. A domain (a building model, a
fleet, a supply chain, a game world) produces its own timeline of `Action`s — as a library (build
in-process, call `Simulator.run`) or as a `--timeline` JSONL file — and the simulator paces it.
Nothing here knows about any particular model.

Idempotent on resume with no platform change: every created Thing is keyed to `stable_id(...)` and a
`Checkpoint` journals the committed prefix, so a re-run skips what it already did and a straggler
duplicate fails loud rather than forking. Structural writes need an editor/admin token
(ModifyData / WritePropertyFact); a service-role token is refused (403).

Usage (as a CLI, playing a serialized timeline):
  export VOS_TOKEN=<editor/admin JWT>        # or VOS_API_KEY=<key> and a token is minted from it
  python3 simulator.py --url http://localhost:5000 --timeline run.jsonl \
      --speed 120 [--seed-first] [--follow] [--checkpoint run.ckpt] [--dry-run]
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import random
import sys
import threading
import time
from dataclasses import dataclass, field

sys.path.insert(0, os.path.dirname(__file__))
from mycelium import MyceliumClient, typed_properties        # noqa: E402


@dataclass
class Action:
    """One paced intent. ``op`` ∈ {apply_fragment, create_thing, create_rel, set_fact, set_observation,
    increment, decrement, ledger_set, delete_thing}; ``args`` is op-specific; ``key`` is a human-readable natural
    key carried for logging and for reading a serialized timeline (the checkpoint keys on the action's
    index, not this); ``seq`` breaks ties so equal-offset actions keep emission order — a Thing is
    always created before the relationship that references it."""
    offset: float
    seq: int
    actor: str
    op: str
    args: dict = field(default_factory=dict)
    key: str = ""

    @property
    def is_setup(self) -> bool:
        """Standing-world actions applied before the paced clock (optionally bulk-loaded via
        POST /api/model). Distinguished by actor, not offset: a paced action may also sit at offset 0."""
        return self.actor == "setup"

    def to_dict(self):
        return {"offset": self.offset, "seq": self.seq, "actor": self.actor,
                "op": self.op, "args": self.args, "key": self.key}

    @classmethod
    def from_dict(cls, d):
        return cls(d["offset"], d["seq"], d["actor"], d["op"], d.get("args", {}), d.get("key", ""))


BATCHED_OPS = frozenset({"create_thing", "create_rel"})
"""The only ops a standing-world batch — a coalesced fragment or a ``--seed-first`` seed document —
can carry."""


def _fragment_thing(args):
    """A create_thing action's args → a ThingDto (Id, Name, typed Properties) for a fragment."""
    return {"Id": args["thing_id"], "Name": args["name"],
            "Properties": typed_properties(args.get("properties"))}


def _fragment_rel(args):
    """A create_rel action's args → a RelDto (Name, Subject, Predicate, Target) for a fragment."""
    return {"Name": args.get("predicate") or "rel", "Subject": args["subject_id"],
            "Predicate": args["predicate_id"], "Target": args["target_id"]}


def load_timeline(path):
    """Read a timeline from a JSONL file (one action per line)."""
    with open(path) as fh:
        return [Action.from_dict(json.loads(line)) for line in fh if line.strip()]


def write_timeline(path, actions):
    with open(path, "w") as fh:
        for action in actions:
            fh.write(json.dumps(action.to_dict()) + "\n")


class BalanceLedger:
    """The simulator's authoritative view of each Thing's quantity balance, so it never asks Mycelium
    to go negative. One lock per balance → adjustments to one Thing are ordered even if the pacer runs
    actors in parallel. A 400 from decrement *inside* the lock means the ledger and Mycelium diverged
    (a lost write, a double-run) and fails loud."""

    def __init__(self, client):
        self._client = client
        self._balance = {}
        self._locks = collections.defaultdict(threading.Lock)

    def set(self, thing_id, amount):
        """Record a starting balance without a write — for a pre-stocked standing world, so the first
        consume resolves against the right absolute quantity."""
        with self._locks[thing_id]:
            self._balance[thing_id] = amount

    def receive(self, thing_id, prop, amount):
        if amount <= 0:
            return 0
        with self._locks[thing_id]:
            self._client.increment(thing_id, prop, amount)
            self._balance[thing_id] = self._balance.get(thing_id, 0) + amount
            return amount

    def consume(self, thing_id, prop, amount):
        """Apply up to ``amount`` without driving the balance negative; return the amount actually
        taken. A short (take < amount) is a legitimate outcome for the caller to record, not an error."""
        with self._locks[thing_id]:
            have = self._balance.get(thing_id, 0)
            take = min(amount, have)
            if take > 0:
                self._client.decrement(thing_id, prop, take)   # a 400 here is a real divergence bug
                self._balance[thing_id] = have - take
            return take

    def balance(self, thing_id):
        return self._balance.get(thing_id, 0)


class Checkpoint:
    """Append-only resume journal keyed by action *index* into the deterministic timeline. The same
    timeline → the same ordered actions → a stable index, so resume skips exactly the committed prefix.
    Deterministic Thing ids are the belt-and-braces for the single in-flight action a crash could
    straddle. Compact: one integer grows, not a per-action set."""

    def __init__(self, path):
        self.path = path
        self.applied = -1
        self.offset = 0.0
        if path and os.path.exists(path):
            with open(path) as fh:
                for line in fh:
                    line = line.strip()
                    if line:
                        index, _, offset = line.partition(",")
                        self.applied = int(index)
                        self.offset = float(offset or 0)

    def record(self, index, offset):
        self.applied = index
        self.offset = offset
        if self.path:
            with open(self.path, "a") as fh:
                fh.write(f"{index},{offset:.3f}\n")
                fh.flush()
                os.fsync(fh.fileno())


class Simulator:
    def __init__(self, client, *, speed=60.0, jitter=0.0, seed_first=False, from_offset=None,
                 checkpoint=None, strict=False, dry_run=False, log_every=200, run_id="sim"):
        self.client = client
        self.speed = speed
        self.jitter = jitter
        self.seed_first = seed_first
        self.from_offset = from_offset
        self.strict = strict
        self.dry_run = dry_run
        self.log_every = log_every
        self.ledger = BalanceLedger(client)
        self.checkpoint = Checkpoint(checkpoint)
        self._jitter_rng = random.Random(f"{run_id}:jitter")
        self.stats = collections.Counter()

    # ── per-op executors ─────────────────────────────────────────────────
    def _apply(self, action):
        op, a = action.op, action.args
        if op == "ledger_set":
            self.ledger.set(a["thing_id"], a["amount"])
        elif op == "apply_fragment":
            # The main path: a partial-model upsert that resolves lazy inheritance (I1) server-side.
            # Idempotent, so it is not wrapped in _guard_duplicate (re-posting is a no-op, not a 409).
            self.client.apply_fragment(a["things"], a["relationships"], a.get("name", "simulator fragment"))
        elif op == "create_thing":
            # Fallback granular path (the main path coalesces creates into apply_fragment). Its own
            # properties go in the create; an `is` subject's collision is now the server's problem.
            self._guard_duplicate(lambda: self.client.create_typed_thing(
                a["name"], a.get("properties"), thing_id=a["thing_id"]))
        elif op == "create_rel":
            self._guard_duplicate(lambda: self.client.create_relationship(
                a["subject_id"], a["predicate_id"], a["target_id"]))
        elif op == "set_fact":
            self.client.set_fact(a["thing_id"], a["prop"], a["value"])
        elif op == "set_observation":
            self.client.set_observation(a["thing_id"], a["prop"], a["value"], a.get("observed_at"))
        elif op == "increment":
            self.ledger.receive(a["thing_id"], a["prop"], a["amount"])
        elif op == "decrement":
            if self.ledger.consume(a["thing_id"], a["prop"], a["amount"]) < a["amount"]:
                self.stats["shortfalls"] += 1
        elif op == "delete_thing":
            self._guard_duplicate(lambda: self.client.delete_thing(a["thing_id"]))
        else:
            raise ValueError(f"unknown op {op!r}")
        self.stats[op] += 1

    def _guard_duplicate(self, call):
        """Re-creating an id or edge that already exists is the expected shape of a resumed/replayed
        action; treat it as a no-op unless strict. Everything else propagates."""
        try:
            call()
        except RuntimeError as err:
            text = str(err).lower()
            if not self.strict and any(s in text for s in ("409", "conflict", "already exists", "duplicate")):
                self.stats["skipped_duplicate"] += 1
                return
            raise

    # ── setup: bulk-load or fast-apply the standing world ────────────────
    def _run_setup(self, setup):
        for action in setup:                       # ledger_set first so balances exist before writes
            if action.op == "ledger_set":
                self.ledger.set(action.args["thing_id"], action.args["amount"])
        writes = [x for x in setup if x.op != "ledger_set"]
        if self.dry_run:
            how = "planned (dry-run)"
        elif self.seed_first:
            document = self._as_seed_document(x for x in writes if x.op in BATCHED_OPS)
            self.client.load_model(document)
            self.stats["seed_first_things"] = len(document["Things"])
            self.stats["seed_first_rels"] = len(document["Relationships"])
            for action in writes:
                if action.op not in BATCHED_OPS:
                    self._apply(action)
            how = "bulk-loaded via POST /api/model"
        else:
            for action in writes:
                self._apply(action)
            how = "applied"
        self.log(f"setup: {len(setup)} actions ({how})")

    @staticmethod
    def _as_seed_document(writes):
        things, rels = [], []
        for action in writes:
            a = action.args
            if action.op == "create_thing":
                things.append({"Id": a["thing_id"], "Name": a["name"],
                               "Properties": typed_properties(a.get("properties"))})
            elif action.op == "create_rel":
                rels.append({"Name": a.get("predicate") or "rel",
                             "Subject": a["subject_id"], "Predicate": a["predicate_id"],
                             "Target": a["target_id"]})
            else:
                raise ValueError(f"{action.op!r} cannot travel in a seed document")
        return {"Name": "simulator standing world", "Things": things, "Relationships": rels}

    # ── coalescing: fold creates (+ their creation-time edges) into fragments ────
    def _coalesce(self, actions):
        """Pure function of the already-sorted input: fold ``create_thing`` (and its creation-time
        ``create_rel`` edges) into ``apply_fragment`` upserts, so each Thing and its ``is`` edges reach
        the server in one partial-model batch and the server resolves lazy inheritance (I1) itself.

        Determinism is the contract: the same sorted timeline always yields the same coalesced list, so
        ``run``'s checkpoint indices (``len(setup) + local``) are stable across runs. Overall
        ``(offset, seq)`` order is preserved, so pacing is unchanged — a paced fragment sits at its
        ``create_thing``'s offset and fires at that instance's moment.

        Setup is coalesced into ONE standing-world fragment *unless* ``--seed-first`` (which bulk-loads
        the granular setup via ``POST /api/model`` and needs create_thing/create_rel left intact). A
        setup action the fragment cannot carry survives as it stands, ordered after the fragment so it
        lands on a Thing that already exists — the paced path's catch-all, applied to both halves."""
        setup = [x for x in actions if x.is_setup]
        paced = [x for x in actions if not x.is_setup]

        # ── SETUP → a single standing-world fragment (ledger_set actions stay, and precede it). ──
        if self.seed_first:
            setup_out = list(setup)                    # leave granular for the seed-document bulk load
        else:
            setup_out = [x for x in setup if x.op == "ledger_set"]
            things = [_fragment_thing(x.args) for x in setup if x.op == "create_thing"]
            rels = [_fragment_rel(x.args) for x in setup if x.op == "create_rel"]
            if things or rels:
                seq = max((x.seq for x in setup), default=0) + 1
                setup_out.append(Action(0.0, seq, "setup", "apply_fragment",
                                        {"things": things, "relationships": rels,
                                         "name": "simulator standing world"}, "setup fragment"))
            setup_out.extend(x for x in setup
                             if x.op != "ledger_set" and x.op not in BATCHED_OPS)

        # ── PACED → each create_thing folds its same-offset creation-time edges into one fragment. ──
        # A create_rel is a creation-time edge of C iff it shares C's offset and its subject is C.
        # Such edges travel with C; a later-offset edge on an already-existing Thing stays granular.
        # Creation position of every Thing, so a forward-referencing edge (target created in a LATER
        # fragment) is not folded into its subject's fragment — where it would apply before the target
        # exists (400 "target does not resolve"). It stays granular and applies via create_relationship
        # once both endpoints exist.
        created_at = {x.args["thing_id"]: (x.offset, x.seq) for x in actions if x.op == "create_thing"}

        edges = collections.defaultdict(list)          # (offset, subject_id) → [paced index]
        for i, x in enumerate(paced):
            if x.op == "create_rel":
                edges[(x.offset, x.args.get("subject_id"))].append(i)
        consumed = set()
        paced_out = []
        for i, x in enumerate(paced):
            if x.op == "create_thing":
                rels = []
                here = (x.offset, x.seq)
                for j in edges.get((x.offset, x.args["thing_id"]), []):
                    target_at = created_at.get(paced[j].args.get("target_id"))
                    if target_at is not None and target_at > here:
                        continue                       # forward reference — leave granular
                    rels.append(_fragment_rel(paced[j].args))
                    consumed.add(j)
                paced_out.append(Action(x.offset, x.seq, x.actor, "apply_fragment",
                                        {"things": [_fragment_thing(x.args)], "relationships": rels,
                                         "name": x.args.get("name") or "fragment"}, x.key))
            elif x.op == "create_rel" and i in consumed:
                continue                               # folded into its subject's fragment
            else:
                paced_out.append(x)
        return setup_out + paced_out

    # ── the pacer ────────────────────────────────────────────────────────
    def run(self, actions):
        actions = sorted(actions, key=lambda a: (a.offset, a.seq))
        # Coalesce BEFORE splitting/indexing: a pure function of the sorted input, so checkpoint
        # indices stay stable across runs (see _coalesce).
        actions = self._coalesce(actions)
        setup = [x for x in actions if x.is_setup]
        paced = [x for x in actions if not x.is_setup]

        if self.from_offset is not None:
            self.log(f"resume: --from-offset {self.from_offset}s")
        elif self.checkpoint.applied >= 0:
            self.log(f"resume: checkpoint at index {self.checkpoint.applied} "
                     f"(offset {self.checkpoint.offset:.0f}s)")

        if len(setup) - 1 > self.checkpoint.applied:
            self._run_setup(setup)
        else:                                      # already committed; still seed the ledger
            for action in setup:
                if action.op == "ledger_set":
                    self.ledger.set(action.args["thing_id"], action.args["amount"])
            self.log("setup: skipped (already committed); ledger reseeded")

        start = time.monotonic()
        for local, action in enumerate(paced):
            index = len(setup) + local
            if index <= self.checkpoint.applied:
                continue
            if self.from_offset is not None and action.offset <= self.from_offset:
                continue
            due = start + action.offset / self.speed + self._jitter_rng.uniform(0, self.jitter)
            delay = due - time.monotonic()
            if delay > 0:
                time.sleep(delay)
            if self.dry_run:
                self.stats["dry_run"] += 1
            else:
                self._apply(action)
            self.checkpoint.record(index, action.offset)
            if index % self.log_every == 0:
                label = action.key or action.args.get("name", "")
                self.log(f"[{action.offset:9.0f}s] #{index} {action.op} {label}")
        self.log(f"done: {dict(self.stats)}")

    def log(self, message):
        print(f"[simulator] {message}", flush=True)


def follow_until(client, predicate, timeout=60.0, selector=None):
    """Subscribe (all changes by default), follow the SSE stream, and return the first event for which
    predicate(kind, data) is true, or None on timeout. Handy for a smoke assertion that a change
    appears within N seconds of the action that caused it."""
    subscription = client.subscribe(selector or {"all": True})
    subscription_id = subscription.get("subscriptionId")
    deadline = time.monotonic() + timeout
    found = {"event": None}

    def _reader():
        for kind, data, _event_id in client.follow(subscription_id):
            if predicate(kind, data):
                found["event"] = (kind, data)
                return

    reader = threading.Thread(target=_reader, daemon=True)
    reader.start()
    reader.join(timeout=max(0.0, deadline - time.monotonic()))
    client.unsubscribe(subscription_id)
    return found["event"]


def main(argv):
    p = argparse.ArgumentParser(description="Replay a timeline of graph changes against a live Mycelium.")
    p.add_argument("--url", default="http://localhost:5000", help="Mycelium base URL")
    p.add_argument("--model-id", default=None, help="target model id for the token mint (?modelId=)")
    p.add_argument("--timeline", required=True, help="JSONL timeline file (one action per line)")
    p.add_argument("--speed", type=float, default=60.0, help="sim-seconds per real-second (default 60)")
    p.add_argument("--jitter", type=float, default=0.0, help="max extra real-seconds of per-action stagger")
    p.add_argument("--run-id", default="sim", help="jitter seed / label")
    p.add_argument("--seed-first", action="store_true",
                   help="bulk-load the standing world via POST /api/model before playback")
    p.add_argument("--from-offset", type=float, default=None, help="skip actions at/under this sim-offset")
    p.add_argument("--checkpoint", default=None, help="resume-journal path (enables crash-resume)")
    p.add_argument("--follow", action="store_true", help="stream SSE change events to stdout while playing")
    p.add_argument("--strict", action="store_true", help="do not swallow duplicate-id/edge errors")
    p.add_argument("--dry-run", action="store_true", help="build and pace the timeline but POST nothing")
    p.add_argument("--log-every", type=int, default=200, help="progress line cadence (actions)")
    args = p.parse_args(argv)

    actions = load_timeline(args.timeline)
    setup = sum(1 for x in actions if x.is_setup)
    print(f"[simulator] timeline: {len(actions)} actions ({setup} setup, {len(actions) - setup} paced), "
          f"speed={args.speed}x, span ~{max((x.offset for x in actions), default=0):.0f}s", flush=True)

    client = MyceliumClient(args.url, model_id=args.model_id)
    if args.follow and not args.dry_run:
        threading.Thread(
            target=lambda: [print(f"[sse] {k} {json.dumps(d)[:120]}", flush=True)
                            for k, d, _ in client.follow(client.subscribe({"all": True})["subscriptionId"])],
            daemon=True).start()

    Simulator(client, speed=args.speed, jitter=args.jitter, seed_first=args.seed_first,
              from_offset=args.from_offset, checkpoint=args.checkpoint, strict=args.strict,
              dry_run=args.dry_run, log_every=args.log_every, run_id=args.run_id).run(actions)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
