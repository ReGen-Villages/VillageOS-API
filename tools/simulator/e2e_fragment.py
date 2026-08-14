#!/usr/bin/env python3
"""End-to-end check: drive the (fragment-emitting) simulator against a LIVE Mycelium and prove that
`POST /api/model/fragment` resolves lazy inheritance (invariant I1) server-side.

It replays a tiny timeline through :class:`simulator.Simulator` — an archetype ``A`` that owns
``code`` (string) + ``capacity`` (decimal), then an instance ``X`` that carries its OWN values for
those names and ``is A``. The simulator coalesces ``X``'s create + its ``is`` edge into a single
``apply_fragment`` (``POST /api/model/fragment``); the server creates ``X`` bare, establishes the
edge, and materializes ``X``'s values as OVERRIDES. Sending ``X`` with own ``code``/``capacity``
BEFORE the edge would trip I1 on the granular path — the whole point is that it does not here.

Asserts, reading ``X`` back:
  * the replay raised no 400 (no I1),
  * ``X``'s own ``Properties`` does NOT contain ``code``/``capacity`` (they are not owned),
  * both appear under ``InheritedProperties`` as overrides, with ``capacity`` still ``250.5``
    (decimal not truncated).
Then it deletes the two test Things. Ids are deterministic (``stable_id``), so the endpoint's upsert
makes re-runs idempotent even if a prior run did not clean up.

Usage:
  export VOS_TOKEN=<editor/admin JWT>        # or VOS_API_KEY=<key> and a token is minted from it
  python3 e2e_fragment.py --url http://localhost:50000 [--model-id <id>]
  # --is-predicate <guid>   pin the built-in `is` predicate Thing id (auto-resolved from the model otherwise)

Exit code 0 = all checks passed, 1 = a check failed or the run errored.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mycelium as M          # noqa: E402
import simulator as S         # noqa: E402


def main(argv) -> int:
    p = argparse.ArgumentParser(description="Live E2E: simulator -> POST /api/model/fragment -> override.")
    p.add_argument("--url", default="http://localhost:50000", help="Mycelium base URL")
    p.add_argument("--model-id", default=None, help="target model id for the token mint")
    p.add_argument("--is-predicate", default=None, help="id of the built-in `is` predicate Thing")
    args = p.parse_args(argv)

    client = M.MyceliumClient(args.url, model_id=args.model_id)

    def get(path):
        req = urllib.request.Request(args.url + path, headers={"Authorization": "Bearer " + client.token()})
        try:
            with urllib.request.urlopen(req, timeout=20) as r:
                return r.status, json.loads(r.read().decode() or "{}")
        except urllib.error.HTTPError as e:
            return e.code, {"_err": e.read().decode()[:300]}

    # ── resolve the built-in `is` predicate Thing id (any Thing named "is") ──
    is_pred = args.is_predicate
    if not is_pred:
        st, rels = get("/api/relationships")
        rels = rels if isinstance(rels, list) else (rels.get("relationships") or rels.get("Relationships") or [])
        for pid in dict.fromkeys((r.get("PredicateId") or r.get("predicateId")) for r in rels):
            if not pid:
                continue
            _, t = get(f"/api/things/{pid}")
            if (t.get("Name") or t.get("name")) == "is":
                is_pred = pid
                break
    if not is_pred:
        print("error: could not resolve the `is` predicate id; pass --is-predicate <guid>", file=sys.stderr)
        return 1
    print(f"[e2e] using `is` predicate id: {is_pred}")

    tag = "e2e-fragment-v1"
    A = M.stable_id(tag, "archetype-A")
    X = M.stable_id(tag, "instance-X")

    # ── timeline: archetype A (setup) + paced instance X that `is` A ──
    # X carries its OWN code/capacity AND its is-edge at the same paced offset, so the simulator folds
    # them into ONE apply_fragment. The server must turn those into overrides (not owned) — no I1.
    timeline = [
        S.Action(0, 0, "setup", "create_thing",
                 {"name": "E2E-Archetype", "thing_id": A,
                  "properties": {"code": "A-DEFAULT", "capacity": 100.0}}, "A"),
        S.Action(1, 1, "worker", "create_thing",
                 {"name": "E2E-Instance", "thing_id": X,
                  "properties": {"code": "X-CODE", "capacity": 250.5}}, "X"),
        S.Action(1, 2, "worker", "create_rel",
                 {"subject_id": X, "predicate_id": is_pred, "predicate": "is", "target_id": A},
                 "X|is|A"),
    ]

    report = {}
    try:
        S.Simulator(client, speed=1e9, run_id="e2e").run(timeline)
        report["replay"] = "OK — no 400 from apply_fragment"
    except Exception as e:  # noqa: BLE001
        report["replay"] = f"FAILED: {e!r}"

    _, thing = get(f"/api/things/{X}")
    own = thing.get("Properties") or {}
    inh = thing.get("InheritedProperties") or {}
    override = next(iter(inh.values()), {}).get("Properties", {}) if inh else {}
    _, eff = get(f"/api/things/{X}/properties")

    def eff_val(suffix):
        return next((v.get("Value") for k, v in (eff or {}).items() if k.endswith(suffix)), None)

    checks = {
        "replay had no 400 (no I1)": report.get("replay", "").startswith("OK"),
        "X does not OWN code/capacity": "code" not in own and "capacity" not in own,
        "code is an override": "code" in override,
        "capacity is an override": "capacity" in override,
        "code overridden to X-CODE": eff_val("code") == "X-CODE",
        "decimal capacity intact (250.5)": eff_val("capacity") == 250.5,
    }

    # ── cleanup ──
    for tid in (X, A):
        try:
            client.delete_thing(tid)
        except RuntimeError:
            pass

    print("\n[e2e] results:")
    for name, ok in checks.items():
        print(f"  {'PASS' if ok else 'FAIL'}  {name}")
    print(f"\n[e2e] own Properties keys: {sorted(own.keys())}")
    print(f"[e2e] override (InheritedProperties) keys: {sorted(override.keys())}")
    print(f"[e2e] effective: {json.dumps(eff, default=str)[:400]}")
    ok = all(checks.values())
    print(f"\n[e2e] RESULT: {'ALL PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
