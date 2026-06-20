"""VillageOS managed-microservice example — Python (FastAPI).

A managed microservice is a handler that Mycelium (the VillageOS gateway)
launches as a daemon and calls when a relationship with the service's predicate
is created. The whole contract is HTTP + a single HS256 JWT.

`is` is NOT an external predicate — Mycelium handles `is` inheritance in-process
and never dispatches it to a handler. Register for a custom predicate instead.

Run:  python app.py --port=5103 --myceliumUrl=https://localhost:7243
"""

from __future__ import annotations

import base64
import sys
import uuid
from contextlib import asynccontextmanager
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from urllib.parse import quote

import httpx
import jwt
from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse

SERVICE_NAME = "Python"


@dataclass
class Config:
    port: int = 0
    mycelium_url: str = ""
    token: str | None = None
    signing_key: str | None = None
    issuer: str = "VillageOS"
    audience: str = "VosClients"


def parse_args(argv: list[str]) -> Config | None:
    cfg = Config()
    seen_port = seen_url = False
    for arg in argv:
        if "=" not in arg:
            continue
        key, value = arg.split("=", 1)
        if key == "--port":
            if not value.isdigit() or not (1 <= int(value) <= 65535):
                return None
            cfg.port, seen_port = int(value), True
        elif key == "--myceliumUrl":
            cfg.mycelium_url, seen_url = value.rstrip("/"), True
        elif key == "--token":
            cfg.token = value
        elif key == "--signingKey":
            cfg.signing_key = value
        elif key == "--issuer" and value:
            cfg.issuer = value
        elif key == "--audience" and value:
            cfg.audience = value
    return cfg if seen_port and seen_url else None


USAGE = (
    "Usage: python app.py --port=<port> --myceliumUrl=<url> "
    "[--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]"
)

config = parse_args(sys.argv[1:]) or Config()
handler_id = str(uuid.uuid4())
mycelium_url_stored = config.mycelium_url
requests_processed = 0


async def _get_token() -> str:
    if config.token:
        return config.token
    async with httpx.AsyncClient(timeout=5, verify=False) as client:
        res = await client.post(f"{config.mycelium_url}/api/auth/token")
        res.raise_for_status()
        return res.json()["token"]


async def register_with_mycelium() -> bool:
    token = await _get_token()
    base = f"http://localhost:{config.port}"
    payload = {
        "handlerId": handler_id,
        "serviceName": SERVICE_NAME,
        "endpointUrl": base,
        "startCommand": "endpoint-service",
        "stopEndpoint": f"{base}/shutdown",
        "healthEndpoint": f"{base}/health",
    }
    async with httpx.AsyncClient(timeout=5, verify=False) as client:
        res = await client.post(
            f"{config.mycelium_url}/api/mycelium/register",
            json=payload,
            headers={"Authorization": f"Bearer {token}"},
        )
        return res.is_success


async def deregister_from_mycelium() -> None:
    try:
        token = await _get_token()
        async with httpx.AsyncClient(timeout=5, verify=False) as client:
            await client.delete(
                f"{config.mycelium_url}/api/mycelium/services/{handler_id}",
                headers={"Authorization": f"Bearer {token}"},
            )
    except Exception as exc:  # best-effort; never block shutdown
        print(f"deregister failed: {exc}", file=sys.stderr)


# ---- Write kinds: Facts · Observations · Sediment ----------------------------------------------
#
# Three ways a microservice writes back to the model, over httpx so the wire contract is explicit.
# Each takes an optional ``client`` for tests (inject an httpx.AsyncClient with a MockTransport);
# production callers omit it. See docs/MICROSERVICE_CONTRACT.md § "Writing data back".


async def _authed_post(path: str, json_body, *, client: httpx.AsyncClient | None = None) -> httpx.Response:
    token = await _get_token()
    url = f"{config.mycelium_url}{path}"
    headers = {"Authorization": f"Bearer {token}"}
    if client is not None:
        return await client.post(url, json=json_body, headers=headers)
    async with httpx.AsyncClient(timeout=30, verify=False) as c:
        return await c.post(url, json=json_body, headers=headers)


async def set_fact(thing_id: str, prop: str, value, *, client: httpx.AsyncClient | None = None) -> int:
    """Assert a structural Fact (synchronous, never lossy). Returns the commit sequence number.
    405 means the property is ObservationOnly; 404 means the thing/property is unknown."""
    res = await _authed_post(f"/api/things/{thing_id}/properties/{quote(prop, safe='')}/facts", {"value": value}, client=client)
    if res.status_code != 201:
        raise RuntimeError(f"fact write returned {res.status_code}")
    return res.json().get("sequenceNumber", 0)


async def record_observation(thing_id: str, prop: str, value, observed_at: str | None = None, *, client: httpx.AsyncClient | None = None) -> None:
    """Record one sampled Observation (queued/batched; 202). Pass observed_at (ISO-8601) for late or
    out-of-order samples, or omit to let Mycelium stamp now. 405 if the property is FactOnly."""
    body = {"value": value}
    if observed_at:
        body["observedAt"] = observed_at
    res = await _authed_post(f"/api/things/{thing_id}/properties/{quote(prop, safe='')}/observations", body, client=client)
    if not res.is_success:
        raise RuntimeError(f"observation write returned {res.status_code}")


async def record_observations(thing_id: str, samples: list[dict], *, client: httpx.AsyncClient | None = None) -> int:
    """Record many samples across an entity's properties in one batch (202). Each sample is a dict
    {property, value, observedAt?}. Returns the accepted-sample count Mycelium reports."""
    if not samples:
        return 0
    res = await _authed_post(f"/api/things/{thing_id}/observations", samples, client=client)
    if not res.is_success:
        raise RuntimeError(f"observation batch returned {res.status_code}")
    return res.json().get("accepted", len(samples))


async def deposit_sediment(readings: list[dict], *, client: httpx.AsyncClient | None = None) -> dict:
    """Bulk-load historical readings straight to sealed Sapwood (202). Each reading is a dict
    {thingId, property, value, observedAt}; entities must already exist and observedAt is required.
    Returns the deposit summary {batchId, series, buckets, samples}."""
    if not readings:
        raise ValueError("at least one reading is required")
    res = await _authed_post("/api/sediment", readings, client=client)
    if not res.is_success:
        raise RuntimeError(f"sediment deposit returned {res.status_code}")
    return res.json()


# ---- Snapshot selector: subscribe to a slice of the model --------------------------------------
#
# The selector replaced launch-time object IDs (the retired ServiceArgs ID template). Reuses the
# _authed_post helper above. See docs/MICROSERVICE_CONTRACT.md § "Selecting a slice".


def slice_by_type_and_traverse(type_: str, predicate: str) -> dict:
    """A representative slice: every Thing of type_ plus its depth-1 predicate neighbours."""
    return {"types": [type_], "traverse": [{"predicate": predicate, "direction": "outgoing", "depth": 1}]}


async def subscribe(selector: dict, *, client: httpx.AsyncClient | None = None) -> dict:
    """POST the selector to /api/subscriptions; return the resolved snapshot closure."""
    res = await _authed_post("/api/subscriptions", selector, client=client)
    if not res.is_success:
        raise RuntimeError(f"subscribe returned {res.status_code}")
    return res.json()


async def unsubscribe(subscription_id: str, *, client: httpx.AsyncClient | None = None) -> None:
    """Release a subscription (best-effort)."""
    token = await _get_token()
    headers = {"Authorization": f"Bearer {token}"}
    url = f"{config.mycelium_url}/api/subscriptions/{subscription_id}"
    if client is not None:
        await client.delete(url, headers=headers)
    else:
        async with httpx.AsyncClient(timeout=10, verify=False) as c:
            await c.delete(url, headers=headers)


async def demo_subscribe(type_: str = "Battery", predicate: str = "powers", *, client: httpx.AsyncClient | None = None) -> dict:
    """Subscribe for a by-type+traverse slice, report the closure, unsubscribe."""
    sub = await subscribe(slice_by_type_and_traverse(type_, predicate), client=client)
    snap = sub.get("snapshot", {})
    things = snap.get("things", [])
    names = [t.get("name") or t.get("id") for t in things]
    await unsubscribe(sub["subscriptionId"], client=client)
    return {
        "subscriptionId": sub["subscriptionId"],
        "watermark": sub.get("watermark"),
        "things": len(things),
        "relationships": len(snap.get("relationships", [])),
        "thingNames": names,
    }


def verify_request(request: Request) -> None:
    """FastAPI dependency validating a Bearer JWT.

    No-op when no signing key was supplied (matches the .NET handlers).
    Issuer/audience/expiry are validated with 30s clock skew, mirroring
    ServiceTokenValidator.
    """
    if not config.signing_key:
        return
    auth = request.headers.get("authorization", "")
    if not auth.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="missing bearer token")
    key = base64.b64decode(config.signing_key)
    try:
        jwt.decode(
            auth[len("Bearer "):],
            key=key,
            algorithms=["HS256"],
            issuer=config.issuer,
            audience=config.audience,
            leeway=30,
        )
    except jwt.PyJWTError as exc:
        raise HTTPException(status_code=401, detail=str(exc)) from exc


@asynccontextmanager
async def lifespan(_: FastAPI):
    try:
        registered = await register_with_mycelium()
        print(f"{'registered' if registered else 'failed to register'} with mycelium as {handler_id}")
    except Exception as exc:
        print(f"registration failed: {exc}", file=sys.stderr)
    yield
    await deregister_from_mycelium()


app = FastAPI(title="VillageOS Python Microservice", lifespan=lifespan)


@app.get("/health")
def health() -> dict:
    return {"status": "Healthy", "service": SERVICE_NAME, "requestsProcessed": requests_processed}


@app.get("/stats")
def stats() -> dict:
    return {
        "service": SERVICE_NAME,
        "version": "1.0.0",
        "requestsProcessed": requests_processed,
        "handlerId": handler_id,
        "myceliumUrl": config.mycelium_url,
    }


@app.post("/handle")
async def handle_relationship(request: Request, _: None = Depends(verify_request)) -> JSONResponse:
    global requests_processed
    requests_processed += 1
    payload = await request.json()
    print(f"handle #{requests_processed}: relationship {payload.get('relationshipId')}")
    return JSONResponse(
        {
            "success": True,
            "service": SERVICE_NAME,
            "requestNumber": requests_processed,
            "relationshipId": payload.get("relationshipId"),
            "status": "handled",
            "echo": payload,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
    )


@app.post("/demo/write-kinds")
async def demo_write_kinds(request: Request, _: None = Depends(verify_request)) -> JSONResponse:
    """Runnable worked example: POST {"thingId": "..."} drives one Fact, one single + one batch
    Observation, and one Sediment deposit against an already-existing Thing."""
    payload = await request.json()
    thing_id = payload.get("thingId")
    if not thing_id:
        raise HTTPException(status_code=400, detail="thingId is required")

    now = datetime.now(timezone.utc)
    day_ago = now - timedelta(days=1)
    seq = await set_fact(thing_id, "status", "active")
    await record_observation(thing_id, "temperature", 21.5, now.isoformat())
    accepted = await record_observations(
        thing_id, [{"property": "temperature", "value": 21.7}, {"property": "flow", "value": 3.1}]
    )
    deposit = await deposit_sediment(
        [
            {"thingId": thing_id, "property": "temperature", "value": 19.8, "observedAt": day_ago.isoformat()},
            {"thingId": thing_id, "property": "temperature", "value": 20.4, "observedAt": (day_ago + timedelta(hours=1)).isoformat()},
        ]
    )
    return JSONResponse(
        {
            "factSequence": seq,
            "observationsAccepted": accepted + 1,
            "sedimentBatchId": deposit.get("batchId"),
            "sedimentSamples": deposit.get("samples"),
        }
    )


@app.post("/demo/subscribe")
async def demo_subscribe_endpoint(request: Request, _: None = Depends(verify_request)) -> JSONResponse:
    """POST {"type": "...", "predicate": "..."} (defaults to Battery/powers); subscribes for that
    slice, returns the resolved snapshot closure, and unsubscribes."""
    try:
        payload = await request.json()
    except Exception:
        payload = {}
    result = await demo_subscribe(payload.get("type", "Battery"), payload.get("predicate", "powers"))
    return JSONResponse(result)


@app.post("/shutdown")
def shutdown(_: None = Depends(verify_request)) -> dict:
    # Mycelium kills the daemon in production; this only acknowledges the request.
    return {"message": f"Shutting down {SERVICE_NAME} microservice"}


def main() -> None:
    if config.port == 0:
        print(USAGE, file=sys.stderr)
        sys.exit(1)
    import uvicorn

    print(
        f"VillageOS {SERVICE_NAME} microservice — port {config.port}, "
        f"mycelium {config.mycelium_url}, auth={bool(config.signing_key)}"
    )
    uvicorn.run(app, host="localhost", port=config.port, log_level="info")


if __name__ == "__main__":
    main()
