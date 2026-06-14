# VillageOS managed microservice — Rust example

A complete VillageOS handler written in **Rust with Axum**. It implements the full managed-microservice contract documented in [docs/MICROSERVICE_AUTHORING.md](../docs/MICROSERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation (`jsonwebtoken`), and graceful deregistration.

It's the Rust analogue of the canonical C# [`vos.ManagedMicroservice.Echo`](../vos.ManagedMicroservice.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace `handle_relationship()` in `src/main.rs` with your own logic.

> ⚠️ **Compile status:** this example was authored against pinned crate versions (axum 0.7, jsonwebtoken 9, reqwest 0.12, tokio 1) but **has not been compiled in this environment** (no Rust toolchain was available when it was written). Run `cargo build` / `cargo test` in CI or locally to verify; the pure logic is covered by `#[cfg(test)]` unit tests.

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
