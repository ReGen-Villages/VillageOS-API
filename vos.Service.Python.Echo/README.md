# VillageOS managed microservice — Python example

A complete, runnable VillageOS handler written in **Python with FastAPI**. It implements the full managed-microservice contract documented in [docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation (PyJWT), and graceful deregistration.

It's the Python analogue of the canonical C# [`vos.Service.CSharp.Echo`](../vos.Service.CSharp.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace `handle_relationship()` in `app.py` with your own logic.

> `is` is **not** an external predicate — Mycelium handles it in-process and never dispatches it. Register your service for a custom predicate (or `consumes`/`produces`). See the authoring doc.

## Run

```bash
pip install -r requirements.txt
python app.py --port=5103 --myceliumUrl=https://localhost:7243

# with inbound auth (as Mycelium launches it):
python app.py --port=5103 --myceliumUrl=https://localhost:7243 \
  --token=<service-jwt> --signingKey=<base64-hmac-key> \
  --issuer=VillageOS --audience=VosClients
```

Interactive OpenAPI docs are available at `/docs` (FastAPI built-in).

## CLI arguments

| Flag | Required | Meaning |
|------|----------|---------|
| `--port` | ✓ | Port to listen on (1–65535) |
| `--myceliumUrl` | ✓ | Base URL of the Mycelium gateway |
| `--token` | | Pre-minted service JWT; if omitted, fetched from `POST /api/auth/token` |
| `--signingKey` | | Base64 HMAC key; when present, `/handle` and `/shutdown` require a valid Mycelium-signed JWT |
| `--issuer` | | JWT issuer (default `VillageOS`) |
| `--audience` | | JWT audience (default `VosClients`) |

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/handle` | JWT* | Process a relationship payload from Mycelium |
| GET | `/health` | — | Liveness probe |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | JWT* | Graceful shutdown |

\* Enforced only when `--signingKey` is supplied.

## How it maps to the contract

- **Registration** — `register_with_mycelium()` POSTs the registration envelope to `/api/mycelium/register` with a bearer token; runs from the FastAPI `lifespan` startup hook.
- **JWT validation** — `verify_request()` (a FastAPI dependency) uses PyJWT to validate the HS256 signature against `base64decode(--signingKey)`, plus issuer/audience/expiry with 30s leeway (matching `ServiceTokenValidator`).
- **Deregistration** — `deregister_from_mycelium()` sends `DELETE /api/mycelium/services/{handler_id}` from the `lifespan` shutdown hook.

## Test

```bash
pip install -r requirements.txt
pytest -v
```

Covers all four endpoints plus JWT validation (valid / missing / tampered / expired / wrong-issuer).

## Writing data back (Facts / Observations / Sediment)

Besides answering `/handle`, a service can write to the model. This example provides an async helper
for each write kind (`set_fact`, `record_observation`, `record_observations`, `deposit_sediment`) and
a runnable demo at `POST /demo/write-kinds {"thingId": "<existing>"}` that drives one of each.

```python
seq = await set_fact(thing_id, "status", "active")                                  # Fact → 201
await record_observation(thing_id, "temperature", 21.5, datetime.now(timezone.utc).isoformat())  # 202
n = await record_observations(thing_id, [{"property": "temperature", "value": 21.7}])
res = await deposit_sediment([{"thingId": thing_id, "property": "temperature",      # bulk → sealed Sapwood
    "value": 19.8, "observedAt": "2026-06-19T12:00:00Z"}])
```

Full wire contract (routes, status codes, 405/404 gating): [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Writing data back".

## Selecting a slice (snapshot selector)

The selector replaced launch-time object IDs: subscribe with a selector describing the slice you
need. This example provides `subscribe` / `unsubscribe` / `slice_by_type_and_traverse` /
`demo_subscribe` and a runnable demo at `POST /demo/subscribe {"type": "Battery", "predicate": "powers"}`.

```python
sub = await subscribe(slice_by_type_and_traverse("Battery", "powers"))
# sub["snapshot"]["things"] / ["relationships"] = exactly the requested closure
```

All selector fields and recipes: [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Selecting a slice".
