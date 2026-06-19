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
from datetime import datetime, timezone

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
