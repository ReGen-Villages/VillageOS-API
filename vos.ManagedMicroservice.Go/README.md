# VillageOS managed microservice — Go example

A complete, runnable VillageOS handler written in **Go using only the standard library** (no third-party modules). It demonstrates the full managed-microservice contract documented in [docs/MICROSERVICE_AUTHORING.md](../docs/MICROSERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation, and graceful deregistration.

This is the Go analogue of the canonical C# [`vos.ManagedMicroservice.Echo`](../vos.ManagedMicroservice.Echo) example — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace the body of `handleRelationship` in `main.go` with your predicate logic.

> Note: `is` is **not** an external predicate — Mycelium handles `is` inheritance in-process and never dispatches it to a handler. Register your service for a custom predicate (or `consumes`/`produces`) instead. See the authoring doc.

## Run

```bash
# Standalone (against a running Mycelium on https://localhost:7243):
go run . --port=5101 --myceliumUrl=https://localhost:7243

# With inbound auth (Mycelium passes these when it launches the daemon):
go run . --port=5101 --myceliumUrl=https://localhost:7243 \
         --token=<service-jwt> --signingKey=<base64-hmac-key> \
         --issuer=VillageOS --audience=VosClients
```

Normally you don't run it by hand — Mycelium launches it as a daemon with these flags when a relationship using your predicate is created.

## CLI arguments

| Flag | Required | Meaning |
|------|----------|---------|
| `--port` | ✓ | Port to listen on (1–65535) |
| `--myceliumUrl` | ✓ | Base URL of the Mycelium gateway |
| `--token` | | Pre-minted service JWT; if omitted, fetched from `POST /api/auth/token` |
| `--signingKey` | | Base64 HMAC key; when present, `/handle` and `/shutdown` require a valid Mycelium-signed JWT |
| `--issuer` | | JWT issuer to validate (default `VillageOS`) |
| `--audience` | | JWT audience to validate (default `VosClients`) |

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/handle` | JWT* | Process a relationship payload from Mycelium |
| GET | `/health` | — | Liveness probe |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | JWT* | Graceful shutdown |

\* Enforced only when `--signingKey` is supplied (matches the .NET handlers).

## How it maps to the contract

- **Registration** — `register()` POSTs to `/api/mycelium/register` with `{handlerId, serviceName, endpointUrl, startCommand, stopEndpoint, healthEndpoint}` after obtaining a bearer token.
- **JWT validation** — `verifyHS256()` checks the HS256 signature against `base64decode(--signingKey)`, plus issuer/audience/expiry with 30s clock skew — the same parameters as `ServiceTokenValidator` on the .NET side.
- **Deregistration** — `deregister()` sends `DELETE /api/mycelium/services/{handlerId}` on SIGINT/SIGTERM and on `/shutdown`.

## Build / verify

```bash
go build ./...
go vet ./...
```

## Docker

```bash
docker build -t vos-microservice-go .
```

See the `Dockerfile` note about `localhost` vs `0.0.0.0` binding for containerized runs.

## Selecting a slice (snapshot selector)

The selector replaced launch-time object IDs: instead of being handed IDs at startup, subscribe with
a selector describing the slice you need. This example provides `subscribe` / `unsubscribe` /
`sliceByTypeAndTraverse` and a runnable demo at `POST /demo/subscribe { "type": "Battery", "predicate": "powers" }`.

```go
sub, _ := s.subscribe(sliceByTypeAndTraverse("Battery", "powers"))
// sub.Snapshot.Things / sub.Snapshot.Relationships = exactly the requested closure
```

All selector fields (ids/names/types/traverse/all + flags) and recipes: [`docs/MICROSERVICE_CONTRACT.md`](../docs/MICROSERVICE_CONTRACT.md) § "Selecting a slice".
