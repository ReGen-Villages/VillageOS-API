#!/usr/bin/env python3
"""simulator — play a timeline of model changes against a live Mycelium, at real time or faster.

A *timeline* is a list of `Action`s in the order of their offsets, the same on every run: each is one
change — create a Thing, create a relationship, post a Fact, adjust a quantity, delete a Thing, post to
a route outside the write API — carrying the `offset`, in simulated seconds, at which it is due. The
simulator waits for each offset (scaled by `--speed`), sends quantity changes through a
`BalanceLedger` so no balance ever goes negative, and posts through the Mycelium client. The platform
stamps every write with the time it commits it, so a post is recorded as happening now, and ranges,
states and the event stream react as they would to a real person or system.

It plays whatever timeline it is handed and knows nothing about any particular model. A caller builds
its own timeline of `Action`s, either in its own process (calling `Simulator.run`) or as a
`--timeline` file of one JSON action per line, and the simulator paces it.

A run can be started again safely: every created Thing is keyed to `stable_id(...)` and a `Checkpoint`
records how far the run got, so a second run skips what the first did, and an action repeated after a
crash fails with an error rather than adding a copy. Changes to Things and relationships need an editor
or admin token (ModifyData, WritePropertyFact); a service's token is refused (403).

Usage, playing a timeline from a file:
  export VOS_TOKEN=<editor or admin token>   # or VOS_API_KEY=<key>, and a token is fetched with it
  python3 simulator.py --url http://localhost:5000 --timeline run.jsonl \
      --speed 120 [--seed-first] [--follow] [--checkpoint run.ckpt] [--dry-run]
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import random
import re
import sys
import threading
import time
from dataclasses import dataclass, field

sys.path.insert(0, os.path.dirname(__file__))
from mycelium import MyceliumClient, typed_properties        # noqa: E402


@dataclass
class Action:
    """One paced change. ``op`` is one of apply_fragment, create_thing, create_rel, set_fact,
    set_observation, increment, decrement, ledger_set, delete_thing and http_post; ``args`` depends on
    ``op``; ``key`` is a readable name kept for the log, for a person reading a saved timeline (the
    checkpoint counts actions by index, not by this) and for a later action to read what an
    ``http_post`` answered; ``seq`` keeps actions at the same offset in the order they were made, so a
    Thing is always created before a relationship to it."""
    offset: float
    seq: int
    actor: str
    op: str
    args: dict = field(default_factory=dict)
    key: str = ""

    @property
    def is_setup(self) -> bool:
        """The actions that set the world up before the paced clock starts (loaded in one call through
        POST /api/model when asked). Told apart by actor, not offset, because a paced action may also sit at
        offset 0."""
        return self.actor == "setup"

    def to_dict(self):
        return {"offset": self.offset, "seq": self.seq, "actor": self.actor,
                "op": self.op, "args": self.args, "key": self.key}

    @classmethod
    def from_dict(cls, d):
        return cls(d["offset"], d["seq"], d["actor"], d["op"], d.get("args", {}), d.get("key", ""))


BATCHED_OPS = frozenset({"create_thing", "create_rel"})
"""The only ops a setup batch — the combined setup fragment or a ``--seed-first`` seed document — can
carry."""

PLACEHOLDER = re.compile(r"\{\{(\w+)\}\}")
"""Where an ``http_post`` writes a value it ``reads`` from an earlier post's answer: ``{{name}}``
anywhere in its address, path, headers or body."""


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
    """The simulator's own record of each Thing's quantity balance, so it never asks Mycelium to go
    negative. Each balance has its own lock, so changes to one Thing stay in order even when actors run
    at the same time. A 400 from a decrement inside the lock means the ledger and Mycelium disagree
    (a lost write, or a run played twice), and the run stops with that error."""

    def __init__(self, client):
        self._client = client
        self._balance = {}
        self._locks = collections.defaultdict(threading.Lock)

    def set(self, thing_id, amount):
        """Record a starting balance without a write, for a world set up already stocked, so the first
        consume is judged against the right quantity."""
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
                self._client.decrement(thing_id, prop, take)   # A 400 here means the ledger and Mycelium disagree.
                self._balance[thing_id] = have - take
            return take

    def balance(self, thing_id):
        return self._balance.get(thing_id, 0)


class Checkpoint:
    """A journal, only ever added to, of how far a run got, counted by the action's index in the
    timeline. The same timeline always gives the same actions in the same order, so a resumed run skips
    exactly the actions already done. Derived Thing ids cover the one action a crash could leave half
    done. One number is kept, not a set of actions."""

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
        self.answers = {}

    def _apply(self, action):
        op, a = action.op, action.args
        if op == "ledger_set":
            self.ledger.set(a["thing_id"], a["amount"])
        elif op == "apply_fragment":
            # Not wrapped in _guard_duplicate, because posting a fragment again changes nothing rather than answering 409.
            self.client.apply_fragment(a["things"], a["relationships"], a.get("name", "simulator fragment"))
        elif op == "create_thing":
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
        elif op == "http_post":
            values = self._values_read(action)
            self.answers[action.key] = self.client.post(
                self._filled(a["path"], values), self._filled(a.get("body"), values),
                self._filled(a.get("headers"), values), self._filled(a.get("address"), values))
        else:
            raise ValueError(f"unknown op {op!r}")
        self.stats[op] += 1

    def _values_read(self, action):
        """``reads`` names a field of an earlier post's answer for each ``{{name}}`` the action
        carries. Answers are kept for the run and not journaled, so a read of a post committed before
        a restart is refused rather than posting the placeholder as a value."""
        values = {}
        for name, read in action.args.get("reads", {}).items():
            if read["from"] not in self.answers:
                raise RuntimeError(f"reads {name!r} from {read['from']!r}, which no post has answered")
            answer = self.answers[read["from"]]
            if read["field"] not in answer:
                raise RuntimeError(f"reads {name!r} from {read['from']!r}, whose answer holds no "
                                   f"{read['field']!r}: {json.dumps(answer)[:200]}")
            values[name] = answer[read["field"]]
        return values

    def _filled(self, value, values):
        if isinstance(value, str):
            return PLACEHOLDER.sub(lambda found: self._value_named(found.group(1), values), value)
        if isinstance(value, dict):
            return {name: self._filled(inner, values) for name, inner in value.items()}
        if isinstance(value, list):
            return [self._filled(inner, values) for inner in value]
        return value

    @staticmethod
    def _value_named(name, values):
        if name not in values:
            raise RuntimeError(f"{{{{{name}}}}} names nothing the action reads")
        return str(values[name])

    def _guard_duplicate(self, call):
        """Creating an id or relationship that already exists is what a resumed or replayed action does,
        so it is skipped unless strict. Every other error is raised."""
        try:
            call()
        except RuntimeError as err:
            text = str(err).lower()
            if not self.strict and any(s in text for s in ("409", "conflict", "already exists", "duplicate")):
                self.stats["skipped_duplicate"] += 1
                return
            raise

    def _run_setup(self, setup):
        for action in setup:                       # Balances are set before any write that changes them.
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

    def _coalesce(self, actions):
        """Folds each ``create_thing``, and the ``create_rel`` actions made with it, into one
        ``apply_fragment``, so a Thing and its ``is`` relationships reach the server in one batch and the
        server stores what the Thing inherits as overrides.

        It reads only the sorted input, so the same timeline always gives the same list, and ``run``'s
        checkpoint indices (``len(setup) + local``) mean the same on every run. ``(offset, seq)`` order is
        kept, so pacing is unchanged: a paced fragment sits at its ``create_thing``'s offset.

        Setup becomes one fragment, unless ``--seed-first`` loads it through ``POST /api/model``, which
        needs the separate create_thing and create_rel actions. A setup action no fragment can carry is
        kept as it is and ordered after the fragment, so it lands on a Thing that already exists."""
        setup = [x for x in actions if x.is_setup]
        paced = [x for x in actions if not x.is_setup]

        if self.seed_first:
            setup_out = list(setup)
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

        # A create_rel travels with the create_thing whose offset it shares and whose Thing is its subject.
        # One whose target is created later stays a separate action, because in its subject's fragment it
        # would be applied before the target exists and refused with 400 "target does not resolve".
        created_at = {x.args["thing_id"]: (x.offset, x.seq) for x in actions if x.op == "create_thing"}

        edges = collections.defaultdict(list)          # (offset, subject_id) -> [paced index]
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
                        continue
                    rels.append(_fragment_rel(paced[j].args))
                    consumed.add(j)
                paced_out.append(Action(x.offset, x.seq, x.actor, "apply_fragment",
                                        {"things": [_fragment_thing(x.args)], "relationships": rels,
                                         "name": x.args.get("name") or "fragment"}, x.key))
            elif x.op == "create_rel" and i in consumed:
                continue
            else:
                paced_out.append(x)
        return setup_out + paced_out

    def run(self, actions):
        actions = sorted(actions, key=lambda a: (a.offset, a.seq))
        # Folded before the actions are split and counted, so a checkpoint's index means the same on every run.
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
        else:                                      # Setup was done by an earlier run, but the ledger still needs its balances.
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
                try:
                    self._apply(action)
                except RuntimeError as refusal:
                    raise RuntimeError(f"{self._named(index, action)} refused: {refusal}") from refusal
            self.checkpoint.record(index, action.offset)
            if index % self.log_every == 0:
                self.log(self._named(index, action))
        self.log(f"done: {dict(self.stats)}")

    @staticmethod
    def _named(index, action):
        label = action.key or action.args.get("name", "")
        return f"action #{index} [{action.offset:.0f}s] {action.op} {label}"

    def log(self, message):
        print(f"[simulator] {message}", flush=True)


def follow_until(client, predicate, timeout=60.0, selector=None):
    """Subscribe (to every change by default), follow the event stream, and return the first event for
    which predicate(kind, data) is true, or None when the time runs out, so a check can wait for a change
    to follow the action that caused it."""
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
