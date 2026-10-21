"""VillageOS managed-microservice example — Python (FastAPI).

A managed microservice is a handler that Mycelium (the VillageOS gateway)
launches as a daemon and calls when a relationship with the service's predicate
is created. The whole contract is HTTP + a single JWT signed on the P-256 elliptic
curve (ES256). The key a handler holds checks a signature and cannot produce one.

The contract itself — arguments, inbound JWT validation, registration, the four
endpoints — is implemented once in vos.Service.Python.Shared. This file is what a
service adds to it: a handler, and any routes of its own.

`is` is NOT an external predicate — Mycelium handles `is` inheritance in-process
and never dispatches it to a handler. Register for a custom predicate instead.

Run:  .venv/bin/python app.py --port=5103 --myceliumUrl=https://localhost:7243
"""

from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone

import httpx
from fastapi import Depends, HTTPException, Request
from fastapi.responses import JSONResponse

from vos_service import Config, ServiceHost, parse_args, sediment_reading

SERVICE_NAME = "Python"

config = parse_args(sys.argv[1:]) or Config()


async def handle_relationship(payload: dict, request_number: int) -> dict:
    print(f"handle #{request_number}: relationship {payload.get('relationshipId')}")
    return {
        "success": True,
        "service": SERVICE_NAME,
        "requestNumber": request_number,
        "relationshipId": payload.get("relationshipId"),
        "status": "handled",
        "echo": payload,
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }


host = ServiceHost(SERVICE_NAME, config, handle_relationship, title="VillageOS Python Microservice")
app = host.app
mycelium = host.mycelium


async def demo_subscribe(type_: str = "Battery", predicate: str = "powers", *, client: httpx.AsyncClient | None = None) -> dict:
    """Subscribe for a by-type+traverse slice, report the closure, unsubscribe."""
    sub = await mycelium.subscribe(mycelium.slice_by_type_and_traverse(type_, predicate), client=client)
    snap = sub.get("snapshot", {})
    things = snap.get("things", [])
    names = [t.get("Name") or t.get("Id") for t in things]
    await mycelium.unsubscribe(sub["subscriptionId"], client=client)
    return {
        "subscriptionId": sub["subscriptionId"],
        "watermark": sub.get("watermark"),
        "things": len(things),
        "relationships": len(snap.get("relationships", [])),
        "thingNames": names,
    }


@app.post("/demo/write-kinds")
async def demo_write_kinds(request: Request, _: None = Depends(host.verify_request)) -> JSONResponse:
    """Drive one of each write kind against an existing Thing. POST {"thingId": "..."}."""
    payload = await request.json()
    thing_id = payload.get("thingId")
    if not thing_id:
        raise HTTPException(status_code=400, detail="thingId is required")

    now = datetime.now(timezone.utc)
    day_ago = now - timedelta(days=1)
    try:
        seq = await mycelium.set_fact(thing_id, "status", "active")
        await mycelium.record_observation(thing_id, "temperature", 21.5, now.isoformat())
        accepted = await mycelium.record_observations(
            thing_id, [{"property": "temperature", "value": 21.7}, {"property": "flow", "value": 3.1}]
        )
        deposit = await mycelium.deposit_sediment(
            [
                sediment_reading(thing_id, "temperature", 19.8, day_ago.isoformat()),
                sediment_reading(thing_id, "temperature", 20.4, (day_ago + timedelta(hours=1)).isoformat()),
            ]
        )
    except (RuntimeError, httpx.HTTPError) as refused:
        return JSONResponse({"error": str(refused)}, status_code=500)
    return JSONResponse(
        {
            "factSequence": seq,
            "observationsAccepted": accepted + 1,
            "sedimentBatchId": deposit.get("batchId"),
            "sedimentSamples": deposit.get("samples"),
        }
    )


@app.post("/demo/subscribe")
async def demo_subscribe_endpoint(request: Request, _: None = Depends(host.verify_request)) -> JSONResponse:
    """Subscribe for a slice and return its closure. POST optional {"type": "...", "predicate": "..."}."""
    try:
        payload = await request.json()
    except Exception:
        payload = {}
    try:
        result = await demo_subscribe(payload.get("type", "Battery"), payload.get("predicate", "powers"))
    except (RuntimeError, httpx.HTTPError) as refused:
        return JSONResponse({"error": str(refused)}, status_code=500)
    return JSONResponse(result)


def main() -> None:
    host.run("app.py")


if __name__ == "__main__":
    main()
