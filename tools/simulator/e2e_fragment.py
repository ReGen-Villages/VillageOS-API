#!/usr/bin/env python3
"""End-to-end check: play the simulator against a live Mycelium and show that
`POST /api/model/fragment` stores a value a Thing gives for an inherited name as an override.

A Thing may not hold a property of its own under a name it inherits. The timeline played here has
an archetype ``A`` that declares ``code`` (text) and ``capacity`` (decimal), then an instance ``X``
that gives its own values for those names and ``is A``. The simulator sends ``X`` and its ``is``
relationship as one ``apply_fragment``; the server creates ``X`` bare, adds the relationship, and
stores ``X``'s values as overrides. Sent as separate creates, ``X``'s own values would arrive before
the relationship and be refused.

Asserts, reading ``X`` back:
  * the run was refused nothing,
  * ``X``'s own ``Properties`` does not contain ``code`` or ``capacity``,
  * both appear under ``InheritedProperties`` as overrides, with ``capacity`` still ``250.5``
    (the decimal not cut to a whole number).
Then it deletes the two test Things. Ids are derived (``stable_id``) and the route updates what is
already there, so the check can run again even if an earlier run did not clean up.

Usage:
  export VOS_TOKEN=<editor or admin token>   # or VOS_API_KEY=<key>, and a token is fetched with it
  python3 e2e_fragment.py --url http://localhost:50000 [--model-id <id>]
  # --is-predicate <id>   the built-in `is` predicate's Thing id (otherwise looked up in the model)

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

    # X carries its own code and capacity and its `is` relationship at one offset, so the simulator sends
    # them as one apply_fragment, and the server must store the values as overrides.
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
