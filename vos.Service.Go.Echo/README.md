# VillageOS managed microservice — Go example

A complete, runnable VillageOS handler written in **Go using only the standard library** (no third-party modules). It demonstrates the full managed-microservice contract documented in [docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md): startup registration, the four required endpoints, inbound JWT validation, and graceful deregistration.

This is the Go analogue of the canonical C# [`vos.Service.CSharp.Echo`](../vos.Service.CSharp.Echo) example — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace the body of `handleRelationship` in `main.go` with your predicate logic.

> Note: `is` is **not** an external predicate — Mycelium handles `is` inheritance in-process and never dispatches it to a handler. Register your service for a custom predicate (or `consumes`/`produces`) instead. See the authoring doc.

## Run

```bash
# Standalone (against a running Mycelium on https://localhost:7243):
go run . --port=5101 --myceliumUrl=https://localhost:7243

# with inbound auth, as Mycelium launches it:
Token=<service-jwt> VerificationKey=<base64-public-key> \
  go run . --port=5101 --myceliumUrl=https://localhost:7243 \
           --issuer=VillageOS --audience=go-echo-handler
```

Normally you don't run it by hand — Mycelium launches it as a daemon when a relationship using your predicate is created, passing the flags on the command line and the credentials on the environment.

## CLI arguments

| Flag | Required | Meaning |
|------|----------|---------|
| `--port` | ✓ | Port to listen on (1–65535) |
| `--myceliumUrl` | ✓ | Base URL of the Mycelium gateway |
| `--issuer` | | JWT issuer to check against; required whenever `VerificationKey` is set |
| `--audience` | | This service's own recipient name, which an inbound token must carry; required whenever `VerificationKey` is set. There is no default |

## Credentials

Both come from the environment and are never flags. A command line is readable by every process on the host and is recorded by anything that logs the line a service was started with, so a `--token=` or `--verificationKey=` argument is ignored.

| Variable | Meaning |
|----------|---------|
| `Token` | Pre-minted service JWT; if unset, fetched from `POST /api/auth/token` |
| `VerificationKey` | Base64 of Mycelium's public signing key; when set, `/handle` and `/shutdown` require a valid Mycelium-signed JWT addressed to this service. It checks a signature and cannot make one |

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/handle` | JWT* | Process a relationship payload from Mycelium |
| POST | `/demo/write-kinds` | JWT* | Demo: write typed-Fact kinds back to Mycelium |
| POST | `/demo/subscribe` | JWT* | Demo: open a snapshot + live SSE subscription |
| GET | `/health` | — | Liveness probe |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | JWT* | Graceful shutdown |

\* Enforced only when a `VerificationKey` is supplied (matches the .NET handlers).

## How it maps to the contract

- **Registration** — `register()` POSTs to `/api/mycelium/register` with `{handlerId, serviceName, endpointUrl, startCommand, stopEndpoint, healthEndpoint}` after obtaining a bearer token.
- **JWT validation** — `verifyES256()` refuses any header naming an algorithm other than ES256, checks the elliptic-curve signature against `base64decode(VerificationKey)`, then checks issuer, this service's own recipient name, and expiry with 30s clock skew — the same parameters as `ServiceTokenValidator` on the .NET side.
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

## Writing data back (Facts / Observations / Sediment)

Besides answering `/handle`, a service can write to the model. This example ships a helper for each
write kind (`setFact`, `recordObservation`, `recordObservations`, `depositSediment`) and a runnable
demo at `POST /demo/write-kinds { "thingId": "<existing>" }` that drives one of each.

```go
seq, _ := s.setFact(thingID, "status", "active")                                  // Fact → 201
_ = s.recordObservation(thingID, "temperature", 21.5, time.Now().UTC().Format(time.RFC3339)) // 202
n, _ := s.recordObservations(thingID, []observationSample{{Property: "temperature", Value: 21.7}})
res, _ := s.depositSediment([]sedimentReading{{ThingID: thingID, Property: "temperature",
    Value: 19.8, ObservedAt: "2026-06-19T12:00:00Z"}})                            // bulk → sealed Sapwood
```

Full wire contract (routes, status codes, 405/404 gating): [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Writing data back".

## Selecting a slice (snapshot selector)

The selector replaced launch-time object IDs: subscribe with a selector describing the slice you
need. This example provides `subscribe` / `unsubscribe` / `sliceByTypeAndTraverse` and a runnable
demo at `POST /demo/subscribe { "type": "Battery", "predicate": "powers" }`.

```go
sub, _ := s.subscribe(sliceByTypeAndTraverse("Battery", "powers"))
// sub.Snapshot.Things / sub.Snapshot.Relationships = exactly the requested closure
```

All selector fields and recipes: [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Selecting a slice".
