# VillageOS managed microservice — Node.js example

A complete, runnable VillageOS handler written in **TypeScript on Node.js using only built-in modules** (`node:http`, `node:crypto`, global `fetch`) — no runtime dependencies. It implements the full managed-microservice contract documented in [docs/MICROSERVICE_AUTHORING.md](../docs/MICROSERVICE_AUTHORING.md).

It's the Node analogue of the canonical C# [`vos.ManagedMicroservice.Echo`](../vos.ManagedMicroservice.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace `handleRelationship` logic in `src/index.ts` with your own.

> `is` is **not** an external predicate — Mycelium handles it in-process. Register your service for a custom predicate (or `consumes`/`produces`). See the authoring doc.

## Run

```bash
npm install            # dev-only deps: typescript + @types/node
npm run build          # compile src → dist
node dist/index.js --port=5102 --myceliumUrl=https://localhost:7243

# with inbound auth (as Mycelium launches it):
node dist/index.js --port=5102 --myceliumUrl=https://localhost:7243 \
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

- **Registration** — `register()` POSTs `{handlerId, serviceName, endpointUrl, startCommand, stopEndpoint, healthEndpoint}` to `/api/mycelium/register` with a bearer token.
- **JWT validation** — `verifyJwt()` checks the HS256 signature against `Buffer.from(signingKey, "base64")`, plus issuer/audience/expiry with 30s clock skew (matching `ServiceTokenValidator`).
- **Deregistration** — `DELETE /api/mycelium/services/{handlerId}` on SIGINT/SIGTERM and `/shutdown`.

## Verify

```bash
npm run typecheck   # tsc --noEmit
```

## Writing data back (Facts / Observations / Sediment)

Besides answering `/handle`, a service can write to the model. This example exports a helper for each
write kind (`setFact`, `recordObservation`, `recordObservations`, `depositSediment`) and a runnable
demo at `POST /demo/write-kinds { "thingId": "<existing>" }` that drives one of each.

```ts
const seq = await setFact(cfg, thingId, "status", "active");                       // Fact → 201
await recordObservation(cfg, thingId, "temperature", 21.5, new Date().toISOString()); // 202
const n = await recordObservations(cfg, thingId, [{ property: "temperature", value: 21.7 }]);
const res = await depositSediment(cfg, [                                           // bulk → sealed Sapwood
  { thingId, property: "temperature", value: 19.8, observedAt: "2026-06-19T12:00:00Z" },
]);
```

Full wire contract (routes, status codes, 405/404 gating): [`docs/MICROSERVICE_CONTRACT.md`](../docs/MICROSERVICE_CONTRACT.md) § "Writing data back".
