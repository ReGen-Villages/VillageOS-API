# VillageOS managed microservice — Python example

A complete, runnable VillageOS handler written in **Python with FastAPI**. It implements the full managed-microservice contract documented in [docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation (PyJWT), and graceful shutdown. The contract itself is implemented once, in [`vos.Service.Python.Shared`](../vos.Service.Python.Shared) (the importable package `vos_service`); this service adds a handler and its own routes to it.

It's the Python analogue of the canonical C# [`vos.Service.CSharp.Echo`](../vos.Service.CSharp.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace `handle_relationship()` in `app.py` with your own logic; a new Python service is a copy of this folder that keeps the `vos_service` import and changes the handler.

> `is` is **not** an external predicate — Mycelium handles it in-process and never dispatches it. Register your service for a custom predicate (or `consumes`/`produces`). See the authoring doc.

## Run

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
ApiKey=<api-key> .venv/bin/python app.py --port=5103 --myceliumUrl=https://localhost:7243

# with inbound auth, as Mycelium launches it (Mycelium hands a launched service a Token):
Token=<service-jwt> VerificationKey=<base64-public-key> \
  .venv/bin/python app.py --port=5103 --myceliumUrl=https://localhost:7243 \
    --issuer=VillageOS --audience=python-echo-handler
```

macOS has no `python` command, and the `python3` on your PATH usually refuses to install packages
into itself, so the virtual environment is not optional.

The service checks the platform's certificate against what the machine trusts, through the
`truststore` package. A Mycelium on the same machine is reached once its development certificate is
trusted (`dotnet dev-certs https --trust`). A platform presenting a certificate the machine does not
trust is refused before the key or token is sent.

Interactive OpenAPI docs are available at `/docs` (FastAPI built-in).

## CLI arguments

| Flag | Required | Meaning |
|------|----------|---------|
| `--port` | ✓ | Port to listen on (1–65535) |
| `--myceliumUrl` | ✓ | Base URL of the Mycelium gateway |
| `--issuer` | | JWT issuer to check against; required whenever `VerificationKey` is set |
| `--audience` | | This service's own recipient name, which an inbound token must carry; required whenever `VerificationKey` is set. There is no default |

## Credentials

All come from the environment and are never flags. A command line is readable by every process on the host and is recorded by anything that logs the line a service was started with, so a `--token=` or `--verificationKey=` argument is ignored.

| Variable | Meaning |
|----------|---------|
| `ApiKey` | An API key, exchanged for a token in the `X-API-Key` header of `POST /api/auth/token`. The token is held and exchanged again thirty seconds before it runs out. A key is used before a `Token` given beside it. A run by hand uses this |
| `Token` | A service JWT, used when no `ApiKey` is set; Mycelium hands one to a service it launches. With neither set, the service makes no call to Mycelium, says so in one line, and still answers its own routes |
| `VerificationKey` | Base64 of Mycelium's public signing key; when set, `/handle` and `/shutdown` require a valid Mycelium-signed JWT addressed to this service. It checks a signature and cannot make one |

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/handle` | JWT* | Process a relationship payload from Mycelium |
| GET | `/health` | — | Liveness probe |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | JWT* | Graceful shutdown |

\* Enforced only when a `VerificationKey` is supplied.

## How it maps to the contract

The pieces below are in `vos.Service.Python.Shared`, which is tested there.

- **Registration** — `MyceliumClient.register()` POSTs the registration envelope to `/api/mycelium/register` with a bearer token; `ServiceHost` runs it from the FastAPI `lifespan` startup hook.
- **JWT validation** — `verifier(config)` returns a FastAPI dependency (`host.verify_request`, which `/handle`, `/shutdown` and this service's demo routes use) that uses PyJWT with `algorithms=["ES256"]` — naming the one algorithm rather than honouring the token's own — against the public key read from `VerificationKey`, plus issuer, this service's own recipient name, and expiry with 30s leeway (matching `ServiceTokenValidator`).
- **Shutdown** — `POST /shutdown` acknowledges and then stops the server, which is how a service Mycelium did not start itself is stopped. The `lifespan` hook makes no call to the broker on the way out. It does not deregister: `DELETE /api/mycelium/services/{handler_id}` is admin-only, and the broker's liveness monitor removes a registration whose service stops answering.

## Test

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m pytest
```

Covers this service's handler and demo endpoints, and that they sit behind the library's token check. The contract itself (every standard endpoint, JWT validation in all its failure modes, argument parsing, registration) is tested in [`vos.Service.Python.Shared`](../vos.Service.Python.Shared).

This is the one Python suite in the repository that runs under pytest; the rest are stdlib
`unittest`. `python3 -m unittest discover` collects nothing here and still reports `OK`, so reach
for the command above rather than the one the other suites use.

## Writing data back (Facts / Observations / Sediment)

Besides answering `/handle`, a service can write to the model. The shared library provides an async
method on `MyceliumClient` for each write kind (`set_fact`, `record_observation`, `record_observations`, `deposit_sediment`) and
a runnable demo at `POST /demo/write-kinds {"thingId": "<existing>"}` that drives one of each.

```python
seq = await mycelium.set_fact(thing_id, "status", "active")                         # Fact → 201
await mycelium.record_observation(thing_id, "temperature", 21.5, datetime.now(timezone.utc).isoformat())  # 202
n = await mycelium.record_observations(thing_id, [{"property": "temperature", "value": 21.7}])
res = await mycelium.deposit_sediment([sediment_reading(thing_id, "temperature", 19.8,   # bulk → sealed Sapwood
    "2026-06-19T12:00:00Z")])
```

Full wire contract (routes, status codes, 405/404 gating): [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Writing data back".

## Selecting a slice (snapshot selector)

A handler is passed no object identifiers at launch: it subscribes with a selector describing the
slice it needs. The shared library provides `MyceliumClient.subscribe` / `unsubscribe` / `slice_by_type_and_traverse`;
this example adds `demo_subscribe` and a runnable demo at `POST /demo/subscribe {"type": "Battery", "predicate": "powers"}`.

```python
sub = await mycelium.subscribe(mycelium.slice_by_type_and_traverse("Battery", "powers"))
# sub["snapshot"]["things"] / ["relationships"] = exactly the requested closure
```

All selector fields and recipes: [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Selecting a slice".
