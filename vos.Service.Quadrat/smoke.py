"""A live smoke test for a running Quadrat: does it read a real raster correctly, over HTTP, as the platform would call it?

    python smoke.py --url http://127.0.0.1:5110 --checks smoke_tatra.json [--token <jwt>]

The checks file is a list. Each check names one node call and what its answer must be:

    {"name": "a point reads the cell under it", "operation": "point", "params": {"resampling": "bilinear"},
     "inputs": {"raster": "tatra-z10-568-350.tif", "lon": 19.9814, "lat": 49.2319},
     "expect": {"outputs.result.value": [1957.64, 1]}}

`expect` maps a dotted path in the answer (a number picks an item of a list: `outputs.result.samples.0.value`) to the value it must hold: a plain value must be equal, and `[value,
tolerance]` must be within the tolerance. A check that must be refused says `"fails": "<code>"` instead. The standard
library only, so it runs from any Python with no environment of its own. It exits 0 when every check passes, else 1.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request

RUN = {"runId": "5a11ef00-0000-4000-8000-000000000001", "nodeId": "5a11ef00-0000-4000-8000-000000000002"}


def _get(url, token):
    return _call(urllib.request.Request(url, headers=_headers(token)))


def _post(url, body, token):
    return _call(urllib.request.Request(url, data=json.dumps(body).encode(), method="POST", headers={**_headers(token), "Content-Type": "application/json"}))


def _headers(token):
    return {"Authorization": f"Bearer {token}"} if token else {}


def _call(request, timeout=120):
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as refused:
        raise RuntimeError(f"HTTP {refused.code} from {request.full_url}: {refused.read().decode(errors='replace')[:300]}") from None
    except (urllib.error.URLError, OSError) as unreachable:
        raise RuntimeError(f"{request.full_url} did not answer: {getattr(unreachable, 'reason', unreachable)}") from None


def run(url, checks, token=None, rasters=None):
    """(name, passed, detail) for the service's health and manifest, then for each check; stops if the service does not answer.

    A raster named without a folder is looked for in `rasters`, so the checks do not depend on where the service was
    started from. An address, or a path that already has a folder, is sent as it is.
    """
    url = url.rstrip("/")
    results = []
    try:
        health = _get(url + "/health", token)
        results.append(("the service is healthy", health.get("status") == "Healthy" and health.get("service") == "Quadrat", json.dumps(health)))
        manifest = _get(url + "/manifest", token)
        operations = next((s["values"] for s in manifest.get("settings", []) if s.get("name") == "operation"), [])
        wanted = {check["operation"] for check in checks}
        missing = sorted(wanted - set(operations))
        results.append(("the manifest names the service and its operations", manifest.get("service") == "Quadrat" and not missing,
                        f"operations offered: {operations}" + (f"; not offered: {missing}" if missing else "")))
    except RuntimeError as unreachable:
        return results + [("the service answers", False, str(unreachable))] if results else [("the service is healthy", False, str(unreachable))]
    for check in checks:
        results.append((check["name"], *_check(url, _placed(check, rasters), token)))
    return results


def _placed(check, rasters):
    raster = check["inputs"].get("raster")
    if rasters is None or not isinstance(raster, str) or "://" in raster or os.path.dirname(raster):
        return check
    return {**check, "inputs": {**check["inputs"], "raster": os.path.join(os.path.abspath(rasters), raster)}}


def _check(url, check, token):
    body = {**RUN, "params": {"operation": check["operation"], **check.get("params", {})}, "inputs": check["inputs"]}
    try:
        answer = _post(url + "/handle", body, token)
    except RuntimeError as unreachable:
        return False, str(unreachable)
    if "fails" in check:
        error = str(answer.get("error") or "")
        if answer.get("result") == "fail" and error.startswith(check["fails"] + ":"):
            return True, error
        return False, f"expected a refusal with code {check['fails']!r}, got {answer.get('result')!r}: {error or json.dumps(answer.get('outputs'))[:200]}"
    if answer.get("result") != "success":
        return False, f"expected an answer, got a refusal: {answer.get('error')}"
    problems = []
    for path, expected in check.get("expect", {}).items():
        found = _at(answer, path)
        if not _matches(found, expected):
            problems.append(f"{path} is {found!r}, expected {expected!r}")
    return (not problems), ("; ".join(problems) or "as expected")


def _at(document, path):
    for part in path.split("."):
        if isinstance(document, dict) and part in document:
            document = document[part]
        elif isinstance(document, list) and part.isdigit() and int(part) < len(document):
            document = document[int(part)]
        else:
            return None
    return document


def _matches(found, expected):
    if isinstance(expected, list):
        value, tolerance = expected
        return isinstance(found, (int, float)) and not isinstance(found, bool) and abs(found - value) <= tolerance
    return found == expected


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--url", required=True, help="where Quadrat is listening, e.g. http://127.0.0.1:5110")
    parser.add_argument("--checks", required=True, metavar="FILE", help="the JSON list of checks to run")
    parser.add_argument("--rasters", metavar="FOLDER", help="where to find a raster the checks name without a folder; the folder the service is told to read")
    parser.add_argument("--token", help="a service JWT, when the service verifies tokens")
    arguments = parser.parse_args(argv)
    with open(arguments.checks, encoding="utf-8") as file:
        checks = json.load(file)
    results = run(arguments.url, checks, arguments.token, arguments.rasters)
    for name, passed, detail in results:
        print(f"{'PASS' if passed else 'FAIL'}  {name}" + ("" if passed else f": {detail}"))
    failed = [name for name, passed, _ in results if not passed]
    print(f"{len(results) - len(failed)} of {len(results)} checks passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
