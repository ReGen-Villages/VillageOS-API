# VillageOS managed microservice — Node.js example

A complete, runnable VillageOS handler written in **TypeScript on Node.js using only built-in modules** (`node:http`, `node:crypto`, global `fetch`) — no runtime dependencies. It implements the full managed-microservice contract documented in [docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md).

It's the Node analogue of the canonical C# [`vos.Service.CSharp.Echo`](../vos.Service.CSharp.Echo) — an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Replace the `/handle` block in `src/index.ts` with your own logic.

> `is` is **not** an external predicate — Mycelium handles it in-process. Register your service for a custom predicate (or `consumes`/`produces`). See the authoring doc.

## Run

```bash
npm install            # dev-only deps: typescript + @types/node
npm run build          # compile src → dist
node dist/index.js --port=5102 --myceliumUrl=https://localhost:7243

# with inbound auth, as Mycelium launches it:
Token=<service-jwt> VerificationKey=<base64-public-key> \
  node dist/index.js --port=5102 --myceliumUrl=https://localhost:7243 \
    --issuer=VillageOS --audience=node-echo-handler
```

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
| GET | `/health` | — | Liveness probe |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | JWT* | Graceful shutdown |

\* Enforced only when a `VerificationKey` is supplied.

## How it maps to the contract

- **Registration** — `register()` POSTs `{handlerId, serviceName, endpointUrl, startCommand, stopEndpoint, healthEndpoint}` to `/api/mycelium/register` with a bearer token.
- **JWT validation** — `verifyJwt()` refuses any header naming an algorithm other than ES256, checks the elliptic-curve signature against the public key read from `VerificationKey`, then checks issuer, this service's own recipient name, and expiry with 30s clock skew (matching `ServiceTokenValidator`).
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

Full wire contract (routes, status codes, 405/404 gating): [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Writing data back".

## Selecting a slice (snapshot selector)

The selector replaced launch-time object IDs: subscribe with a selector describing the slice you
need. This example exports `subscribe` / `unsubscribe` / `sliceByTypeAndTraverse` / `demoSubscribe`
and a runnable demo at `POST /demo/subscribe { "type": "Battery", "predicate": "powers" }`.

```ts
const sub = await subscribe(cfg, sliceByTypeAndTraverse("Battery", "powers"));
// sub.snapshot.things / sub.snapshot.relationships = exactly the requested closure
```

All selector fields and recipes: [`docs/SERVICE_CONTRACT.md`](../docs/SERVICE_CONTRACT.md) § "Selecting a slice".
