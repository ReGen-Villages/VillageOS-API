# VillageOS managed microservice — Rust example

A complete VillageOS handler written in **Rust with Axum**. It implements the full managed-microservice contract documented in [docs/MICROSERVICE_AUTHORING.md](../docs/MICROSERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation (`jsonwebtoken`), and graceful deregistration.

It's the Rust analogue of the canonical C# [`vos.ManagedMicroservice.CSharp.Echo`](../vos.ManagedMicroservice.CSharp.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace `handle_relationship()` in `src/main.rs` with your own logic.

> `is` is **not** an external predicate — Mycelium handles it in-process. Register your service for a custom predicate (or `consumes`/`produces`). See the authoring doc.

## Run

```bash
cargo run -- --port=5104 --myceliumUrl=https://localhost:7243

# with inbound auth (as Mycelium launches it):
cargo run -- --port=5104 --myceliumUrl=https://localhost:7243 \
  --token=<service-jwt> --signingKey=<base64-hmac-key> \
  --issuer=VillageOS --audience=VosClients
```

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

- **Registration** — `register()` POSTs the registration envelope to `/api/mycelium/register` with a bearer token, spawned once the listener is bound.
- **JWT validation** — `verify_jwt()` uses `jsonwebtoken` to validate the HS256 signature against `base64::decode(--signingKey)`, plus issuer/audience/expiry with 30s leeway (matching `ServiceTokenValidator`).
- **Deregistration** — `DELETE /api/mycelium/services/{handler_id}` from Axum's graceful-shutdown hook on Ctrl-C.

## Verify

```bash
cargo build
cargo test     # unit tests for parse_args + verify_jwt
```

## Writing data back (Facts / Observations / Sediment)

Besides answering `/handle`, a service can write to the model. This example provides an async helper
for each write kind (`set_fact`, `record_observation`, `record_observations`, `deposit_sediment`) and
a runnable demo at `POST /demo/write-kinds { "thingId": "<existing>" }` that drives one of each.

```rust
let seq = set_fact(cfg, &http, thing_id, "status", json!("active")).await?;        // Fact → 201
record_observation(cfg, &http, thing_id, "temperature", json!(21.5), Some("2026-06-20T12:00:00Z")).await?;
let n = record_observations(cfg, &http, thing_id,
    &[ObservationSample { property: "temperature".into(), value: json!(21.7), observed_at: None }]).await?;
let res = deposit_sediment(cfg, &http,                                             // bulk → sealed Sapwood
    &[SedimentReading { thing_id: thing_id.into(), property: "temperature".into(),
        value: json!(19.8), observed_at: "2026-06-19T12:00:00Z".into() }]).await?;
```

Full wire contract (routes, status codes, 405/404 gating): [`docs/MICROSERVICE_CONTRACT.md`](../docs/MICROSERVICE_CONTRACT.md) § "Writing data back".

## Selecting a slice (snapshot selector)

The selector replaced launch-time object IDs: subscribe with a selector describing the slice you
need. This example provides `subscribe` / `unsubscribe` / `slice_by_type_and_traverse` and a
runnable demo at `POST /demo/subscribe { "type": "Battery", "predicate": "powers" }`.

```rust
let sub = subscribe(cfg, &http, &slice_by_type_and_traverse("Battery", "powers")).await?;
// sub.snapshot.things / sub.snapshot.relationships = exactly the requested closure
```

All selector fields and recipes: [`docs/MICROSERVICE_CONTRACT.md`](../docs/MICROSERVICE_CONTRACT.md) § "Selecting a slice".
