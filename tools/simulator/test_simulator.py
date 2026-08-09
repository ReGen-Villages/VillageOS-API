"""Behavioural specification for the generic simulator: action serialization, the BalanceLedger's
one-writer-per-balance guarantee, resumable checkpoints, and a dry replay against a fake Mycelium
that fails loud on any oversell. Domain-agnostic — the timelines here are abstract Things and bins.
"""
import io
import json
import os
import tempfile
import threading
import unittest
import urllib.request

import mycelium as M
import simulator as S


def _unwrap(value):
    return value["value"] if isinstance(value, dict) else value


class FakeMycelium:
    """Records every call and maintains per-thing balances, raising a 400 (as Mycelium's Adjust does)
    if a decrement would go negative — so a passing replay proves the ledger never oversold."""

    def __init__(self):
        self.things, self.rels, self.facts, self.balances, self.order = {}, [], [], {}, []
        self.observations, self.deleted, self.fragments = [], [], []
        self._lock = threading.Lock()

    def create_typed_thing(self, name, properties=None, thing_id=None):
        with self._lock:
            if thing_id in self.things:
                raise RuntimeError("POST /api/things -> 409 duplicate id")
            properties = properties or {}
            self.things[thing_id] = properties
            if "contained_units" in properties:
                self.balances[thing_id] = _unwrap(properties["contained_units"])
            self.order.append(("thing", thing_id))

    def create_relationship(self, subject_id, predicate_id, target_id):
        with self._lock:
            self.rels.append((subject_id, predicate_id, target_id))
            self.order.append(("rel", subject_id, target_id))

    def set_fact(self, thing_id, prop, value):
        with self._lock:
            self.facts.append((thing_id, prop, value))

    def apply_fragment(self, things, relationships, name="simulator fragment"):
        """Record the fragment and apply it the way the server would from the client's view: upsert
        each Thing (storing its typed Properties, seeding a contained_units balance as
        create_typed_thing does) and each Relationship. Upsert/idempotent — a re-post is a no-op."""
        with self._lock:
            self.fragments.append({"name": name, "things": things, "relationships": relationships})
            for t in things:
                properties = t.get("Properties", {})
                self.things[t["Id"]] = properties
                if "contained_units" in properties:
                    self.balances[t["Id"]] = _unwrap(properties["contained_units"])
                self.order.append(("thing", t["Id"]))
            for r in relationships:
                self.rels.append((r["Subject"], r["Predicate"], r["Target"]))
                self.order.append(("rel", r["Subject"], r["Target"]))

    def set_observation(self, thing_id, prop, value, observed_at=None):
        with self._lock:
            self.observations.append((thing_id, prop, value, observed_at))

    def increment(self, thing_id, prop, amount):
        with self._lock:
            self.balances[thing_id] = self.balances.get(thing_id, 0) + amount

    def decrement(self, thing_id, prop, amount):
        with self._lock:
            have = self.balances.get(thing_id, 0)
            if amount > have:
                raise RuntimeError(f"POST decrements -> 400 available={have} requested={amount}")
            self.balances[thing_id] = have - amount

    def delete_thing(self, thing_id):
        with self._lock:
            self.deleted.append(thing_id)
            self.things.pop(thing_id, None)

    def load_model(self, document):
        with self._lock:
            for t in document["Things"]:
                properties = t.get("Properties", {})
                self.things[t["Id"]] = properties
                if "contained_units" in properties:
                    self.balances[t["Id"]] = _unwrap(properties["contained_units"])
            for r in document["Relationships"]:
                self.rels.append((r["Subject"], r["Predicate"], r["Target"]))

    # A double that yielded an empty stream would pass every assertion while testing nothing.
    _NO_STREAM = ("This double records writes and does not emulate the change stream. "
                  "Subscribe, follow and unsubscribe are server behaviour: test them against a "
                  "running Mycelium, the way e2e_fragment.py does.")

    def subscribe(self, selector):
        raise NotImplementedError(self._NO_STREAM)

    def follow(self, subscription_id, last=0, token_in_query=False):
        raise NotImplementedError(self._NO_STREAM)

    def unsubscribe(self, subscription_id):
        raise NotImplementedError(self._NO_STREAM)


def _timeline():
    """A tiny generic timeline: a stocked bin, then paced widgets that draw it down past empty."""
    acts = [
        S.Action(0, 0, "setup", "create_thing",
                 {"name": "BIN", "thing_id": "bin", "properties": {"contained_units": 10}}, "BIN"),
        S.Action(0, 1, "setup", "ledger_set", {"thing_id": "bin", "amount": 10}, "BIN"),
    ]
    seq = 2
    for i in range(6):                              # 6 widgets x 2 units = 12 requested > 10 available
        acts.append(S.Action(float(i), seq, "worker", "create_thing",
                    {"name": f"W{i}", "thing_id": f"w{i}", "properties": {"n": i}}, f"W{i}"))
        seq += 1
        acts.append(S.Action(float(i), seq, "worker", "create_rel",
                    {"subject_id": f"w{i}", "predicate_id": "has", "target_id": "bin"}, f"W{i}|has|bin"))
        seq += 1
        acts.append(S.Action(float(i), seq, "worker", "decrement",
                    {"thing_id": "bin", "prop": "contained_units", "amount": 2}, f"draw{i}"))
        seq += 1
    return acts


def _fast(**kw):
    kw.setdefault("speed", 1e9)
    kw.setdefault("run_id", "test")
    return kw


class AuthMintsFromApiKeyViaHeader(unittest.TestCase):
    """The mint contract is POST /api/auth/token with the key in the X-API-Key header (optional
    ?modelId=), returning {token}. Guards the fix from the earlier wrong {"apiKey": ...} body form."""

    def _capture(self, response_json):
        captured = {}

        class _Resp(io.BytesIO):
            def __enter__(self_):
                return self_

            def __exit__(self_, *a):
                return False

        def fake_urlopen(req, timeout=None, **kwargs):
            captured["method"] = req.get_method()
            captured["url"] = req.full_url
            captured["headers"] = {k.lower(): v for k, v in req.header_items()}
            captured["body"] = req.data
            return _Resp(json.dumps(response_json).encode())

        return captured, fake_urlopen

    def test_ready_token_is_used_verbatim_without_a_mint(self):
        client = M.MyceliumClient("http://h", token="ready-jwt")
        self.assertEqual(client.token(), "ready-jwt")

    def test_api_key_is_exchanged_via_x_api_key_header(self):
        captured, fake = self._capture({"token": "minted-jwt"})
        original = urllib.request.urlopen
        urllib.request.urlopen = fake
        try:
            client = M.MyceliumClient("http://h", api_key="KEY-123", model_id="m-1")
            self.assertEqual(client.token(), "minted-jwt")
        finally:
            urllib.request.urlopen = original
        self.assertEqual(captured["method"], "POST")
        self.assertIn("/api/auth/token", captured["url"])
        self.assertIn("modelid=m-1", captured["url"].lower())
        self.assertEqual(captured["headers"].get("x-api-key"), "KEY-123")
        self.assertIsNone(captured["body"])              # key rides the header, not a JSON body

    def test_no_credentials_raises(self):
        with self.assertRaises(RuntimeError):
            M.MyceliumClient("http://h").token()


class ExpiredTokenIsReminted(unittest.TestCase):
    """A cached JWT expires mid-run, so a long scenario would die with 401 on its first write
    past the token's TTL. An authenticated 401 re-mints once from the API key and retries."""

    def _client_with(self, responses):
        """Drive urlopen from a scripted list of (status, payload) and record every request.
        status None means a normal 200 returning payload."""
        calls = []

        class _Resp(io.BytesIO):
            def __enter__(self_):
                return self_

            def __exit__(self_, *a):
                return False

        def fake_urlopen(req, timeout=None, **kwargs):
            calls.append({
                "url": req.full_url,
                "headers": {k.lower(): v for k, v in req.header_items()},
            })
            status, payload = responses.pop(0)
            if status is not None:
                raise urllib.error.HTTPError(req.full_url, status, "err", {}, io.BytesIO(b"denied"))
            return _Resp(json.dumps(payload).encode())

        return calls, fake_urlopen

    def test_401_remints_the_token_and_retries_once(self):
        calls, fake = self._client_with([
            (None, {"token": "first-jwt"}),      # initial mint
            (401, None),                         # the cached token has expired
            (None, {"token": "second-jwt"}),     # re-mint
            (None, {"ok": True}),                # retry succeeds
        ])
        original = urllib.request.urlopen
        urllib.request.urlopen = fake
        try:
            client = M.MyceliumClient("http://h", api_key="KEY-123")
            result = client._json("POST", "/api/things", {"Name": "T"})
        finally:
            urllib.request.urlopen = original

        self.assertEqual(result, {"ok": True})
        writes = [c for c in calls if "/api/things" in c["url"]]
        self.assertEqual(len(writes), 2)
        self.assertEqual(writes[0]["headers"].get("authorization"), "Bearer first-jwt")
        self.assertEqual(writes[1]["headers"].get("authorization"), "Bearer second-jwt")

    def test_a_second_401_surfaces_rather_than_looping(self):
        calls, fake = self._client_with([
            (None, {"token": "first-jwt"}),
            (401, None),
            (None, {"token": "second-jwt"}),
            (401, None),                         # still refused — the key itself is not accepted
        ])
        original = urllib.request.urlopen
        urllib.request.urlopen = fake
        try:
            client = M.MyceliumClient("http://h", api_key="KEY-123")
            with self.assertRaises(RuntimeError):
                client._json("POST", "/api/things", {"Name": "T"})
        finally:
            urllib.request.urlopen = original

        self.assertEqual(len([c for c in calls if "/api/things" in c["url"]]), 2)

    def test_401_without_an_api_key_is_not_retried(self):
        calls, fake = self._client_with([(401, None)])
        original = urllib.request.urlopen
        urllib.request.urlopen = fake
        try:
            client = M.MyceliumClient("http://h", token="ready-jwt")
            with self.assertRaises(RuntimeError):
                client._json("POST", "/api/things", {"Name": "T"})
        finally:
            urllib.request.urlopen = original

        self.assertEqual(len(calls), 1)          # nothing to re-mint from, so it surfaces at once


class ActionSerialization(unittest.TestCase):
    def test_roundtrips_through_jsonl(self):
        acts = _timeline()
        with tempfile.NamedTemporaryFile(suffix=".jsonl", delete=False) as fh:
            path = fh.name
        try:
            S.write_timeline(path, acts)
            back = S.load_timeline(path)
            self.assertEqual([a.to_dict() for a in acts], [a.to_dict() for a in back])
        finally:
            os.unlink(path)

    def test_setup_is_tagged_by_actor(self):
        acts = _timeline()
        self.assertTrue(all(a.is_setup for a in acts if a.actor == "setup"))
        self.assertFalse(any(a.is_setup for a in acts if a.actor == "worker"))


class BalanceLedgerNeverOversells(unittest.TestCase):
    def test_concurrent_consumers_short_and_never_go_negative(self):
        client = FakeMycelium()
        client.balances["bin"] = 100
        ledger = S.BalanceLedger(client)
        ledger.set("bin", 100)
        taken, lock = [], threading.Lock()

        def consume():
            got = ledger.consume("bin", "contained_units", 3)   # 50 x 3 = 150 requested
            with lock:
                taken.append(got)

        threads = [threading.Thread(target=consume) for _ in range(50)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        self.assertEqual(sum(taken), 100)
        self.assertEqual(client.balances["bin"], 0)
        self.assertEqual(ledger.balance("bin"), 0)


class CheckpointResumes(unittest.TestCase):
    def test_records_and_reloads(self):
        with tempfile.NamedTemporaryFile(suffix=".ckpt", delete=False) as fh:
            path = fh.name
        try:
            cp = S.Checkpoint(path)
            self.assertEqual(cp.applied, -1)
            cp.record(4, 40.0)
            cp.record(8, 80.0)
            self.assertEqual(S.Checkpoint(path).applied, 8)
            self.assertEqual(S.Checkpoint(path).offset, 80.0)
        finally:
            os.unlink(path)

    def test_a_resumed_run_skips_the_committed_prefix(self):
        with tempfile.NamedTemporaryFile(suffix=".ckpt", delete=False) as fh:
            path = fh.name
        try:
            acts = _timeline()
            first = FakeMycelium()
            S.Simulator(first, **_fast(checkpoint=path)).run(acts)
            self.assertGreater(len(first.things), 0)
            second = FakeMycelium()
            S.Simulator(second, **_fast(checkpoint=path)).run(acts)
            self.assertEqual(len(second.things), 0)     # everything already committed
        finally:
            os.unlink(path)


class ReplayAgainstFakeMycelium(unittest.TestCase):
    def setUp(self):
        self.client = FakeMycelium()
        S.Simulator(self.client, **_fast()).run(_timeline())

    def test_no_decrement_ever_oversold(self):
        for balance in self.client.balances.values():
            self.assertGreaterEqual(balance, 0)
        self.assertEqual(self.client.balances["bin"], 0)    # 10 stocked, 12 requested → floored at 0

    def test_relationship_subjects_are_created_before_referenced(self):
        first_created = {}
        for position, entry in enumerate(self.client.order):
            if entry[0] == "thing":
                first_created.setdefault(entry[1], position)
        for position, entry in enumerate(self.client.order):
            if entry[0] == "rel" and entry[1] in first_created:
                self.assertLess(first_created[entry[1]], position)

    def test_seed_first_bulk_loads_setup(self):
        client = FakeMycelium()
        S.Simulator(client, **_fast(seed_first=True)).run(_timeline())
        self.assertIn("bin", client.things)                 # standing world loaded via load_model
        for balance in client.balances.values():
            self.assertGreaterEqual(balance, 0)


class TheChangeStreamIsRefusedNotFaked(unittest.TestCase):
    """`follow_until` subscribes, reads the SSE stream and unsubscribes — server behaviour a
    recorder cannot stand in for."""

    def test_each_watch_call_says_what_is_not_emulated(self):
        client = FakeMycelium()

        for call in (lambda: client.subscribe({"all": True}),
                     lambda: list(client.follow("any-subscription")),
                     lambda: client.unsubscribe("any-subscription")):
            with self.assertRaises(NotImplementedError) as raised:
                call()
            self.assertIn("change stream", str(raised.exception))

    def test_follow_until_against_the_double_refuses_rather_than_timing_out(self):
        with self.assertRaises(NotImplementedError):
            S.follow_until(FakeMycelium(), lambda kind, data: True, timeout=0.1)


class EveryOpIsApplied(unittest.TestCase):
    """Exercise the full op vocabulary against the fake, including the ones the reference domain does
    not emit (set_observation, delete_thing), so the executor's branches stay covered."""

    def test_observation_and_delete_ops_reach_the_client(self):
        client = FakeMycelium()
        actions = [
            S.Action(0, 0, "setup", "create_thing",
                     {"name": "sensor", "thing_id": "s1", "properties": {"temp": 4}}, "sensor"),
            S.Action(1, 1, "telemetry", "set_observation",
                     {"thing_id": "s1", "prop": "temp", "value": 5, "observed_at": "2026-01-01T00:00:00Z"}, "t"),
            S.Action(2, 2, "cleanup", "delete_thing", {"thing_id": "s1"}, "gone"),
        ]
        S.Simulator(client, **_fast()).run(actions)
        self.assertEqual(client.observations, [("s1", "temp", 5, "2026-01-01T00:00:00Z")])
        self.assertEqual(client.deleted, ["s1"])
        self.assertNotIn("s1", client.things)

    def test_unknown_op_raises(self):
        with self.assertRaises(ValueError):
            S.Simulator(FakeMycelium(), **_fast()).run([S.Action(0, 0, "x", "frobnicate", {}, "k")])


class FragmentCarriesInheritorWithItsIsEdge(unittest.TestCase):
    """Lazy inheritance (I1: a Thing may not *own* a property name it *inherits*) is now resolved
    SERVER-SIDE. The simulator no longer choreographs bare-create-then-override; it coalesces each
    ``create_thing`` and its creation-time ``create_rel`` edges (same offset, subject == the thing)
    into ONE ``apply_fragment`` (``POST /api/model/fragment``), so the Thing and its ``is`` edge travel
    together and the server creates the Thing bare, establishes the edge, and materializes any
    inherited value as an override. Setup collapses into a single standing-world fragment."""

    def _run(self, actions):
        client = FakeMycelium()
        S.Simulator(client, **_fast()).run(actions)
        return client

    def _instance(self, props):
        return [
            S.Action(0, 0, "setup", "create_thing", {"name": "A", "thing_id": "A"}, "A"),   # archetype
            S.Action(1, 1, "worker", "create_thing", {"name": "X", "thing_id": "X", "properties": props}, "X"),
            S.Action(1, 2, "worker", "create_rel",
                     {"subject_id": "X", "predicate_id": "GUID-is", "predicate": "is", "target_id": "A"}, "X|is|A"),
        ]

    def _fragment_with(self, client, thing_id):
        return next(f for f in client.fragments if any(t["Id"] == thing_id for t in f["things"]))

    def test_thing_and_its_is_edge_travel_in_one_fragment(self):
        client = self._run(self._instance({"code": "x"}))
        frag = self._fragment_with(client, "X")                          # the paced instance's fragment
        self.assertIn("X", {t["Id"] for t in frag["things"]})
        edges = [(r["Subject"], r["Predicate"], r["Target"]) for r in frag["relationships"]]
        self.assertIn(("X", "GUID-is", "A"), edges)                      # is-edge rides with the Thing
        x = next(t for t in frag["things"] if t["Id"] == "X")
        self.assertEqual(_unwrap(x["Properties"]["code"]), "x")          # X's own property carried too

    def test_decimal_property_is_carried_as_a_typed_envelope(self):
        client = self._run(self._instance({"weight": {"typeInfo": "vos.Decimal", "value": 2.5}}))
        x = next(t for t in self._fragment_with(client, "X")["things"] if t["Id"] == "X")
        self.assertEqual(x["Properties"]["weight"], {"typeInfo": "vos.Decimal", "value": 2.5})

    def test_a_later_offset_is_edge_stays_granular(self):
        # An `is` edge at a LATER offset than the Thing is a lifecycle edge on an already-existing
        # Thing; it is not a creation-time edge, so it stays a granular create_rel, not folded in.
        client = self._run([
            S.Action(0, 0, "setup", "create_thing", {"name": "A", "thing_id": "A"}, "A"),
            S.Action(1, 1, "worker", "create_thing",
                     {"name": "Y", "thing_id": "Y", "properties": {"code": "y"}}, "Y"),
            S.Action(5, 2, "worker", "create_rel",
                     {"subject_id": "Y", "predicate_id": "GUID-is", "predicate": "is", "target_id": "A"}, "Y|is|A"),
        ])
        self.assertEqual(self._fragment_with(client, "Y")["relationships"], [])   # edge not folded in
        self.assertIn(("Y", "GUID-is", "A"), client.rels)                # applied granularly instead

    def test_setup_is_emitted_as_a_single_bulk_fragment(self):
        client = self._run(self._instance({"code": "x"}))
        setup_frags = [f for f in client.fragments if any(t["Id"] == "A" for t in f["things"])]
        self.assertEqual(len(setup_frags), 1)                            # one standing-world upsert
        self.assertEqual({t["Id"] for t in setup_frags[0]["things"]}, {"A"})


class CliPlaysATimelineFile(unittest.TestCase):
    def test_dry_run_over_a_jsonl_timeline_makes_no_calls(self):
        with tempfile.NamedTemporaryFile(suffix=".jsonl", delete=False) as fh:
            path = fh.name
        try:
            S.write_timeline(path, _timeline())
            self.assertEqual(S.main(["--timeline", path, "--dry-run", "--speed", "1e9"]), 0)
        finally:
            os.unlink(path)


if __name__ == "__main__":
    unittest.main()


class CoalesceForwardReference(unittest.TestCase):
    """A create_rel whose target is created in a LATER fragment must not fold into its subject's
    fragment (it would apply before the target exists → 400); it stays granular."""

    def test_forward_referencing_edge_stays_granular(self):
        sim = S.Simulator(FakeMycelium(), **_fast())
        A = S.Action
        actions = [
            A(1, 0, "w", "create_thing", {"thing_id": "W", "name": "W"}, "W"),
            A(1, 1, "w", "create_rel",
              {"subject_id": "W", "predicate_id": "contains", "target_id": "O"}, "WO"),
            A(1, 2, "w", "create_thing", {"thing_id": "O", "name": "O"}, "O"),
        ]
        out = sim._coalesce(actions)

        w_frag = next(a for a in out
                      if a.op == "apply_fragment" and a.args["things"][0]["Id"] == "W")
        self.assertEqual(w_frag.args["relationships"], [],
                         "the forward-referencing W->O edge must not fold into W's fragment")
        self.assertTrue(
            any(a.op == "create_rel" and a.args.get("target_id") == "O" for a in out),
            "the W->O edge must survive as a granular create_rel applied after O exists")
