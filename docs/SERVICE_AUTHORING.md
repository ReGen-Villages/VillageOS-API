# Authoring a VillageOS managed microservice

A **managed microservice** is an external handler that Mycelium (the VillageOS gateway) launches as a daemon and calls when a relationship using the service's predicate is created. The entire contract is **HTTP + a single HS256 JWT** — so a handler can be written in *any* language with an HTTP server and an HMAC-SHA256 library.

This document is the language-agnostic contract. Working reference implementations live alongside it:

| Language | Project | Style | Verified |
|----------|---------|-------|----------|
| C# / .NET | [`vos.Service.CSharp.Echo`](../vos.Service.CSharp.Echo) | ASP.NET minimal API (canonical) | ✓ build + tests |
| Go | [`vos.Service.Go.Echo`](../vos.Service.Go.Echo) | standard library, zero deps | ✓ build + tests |
| Node / TypeScript | [`vos.Service.Node.Echo`](../vos.Service.Node.Echo) | built-ins, zero runtime deps | ✓ typecheck + tests |
| Python | [`vos.Service.Python.Echo`](../vos.Service.Python.Echo) | FastAPI | ✓ pytest |
| Rust | [`vos.Service.Rust.Echo`](../vos.Service.Rust.Echo) | Axum | ✓ build + tests + clippy |

Each reference is an **echo handler**: `/handle` acknowledges the relationship and reflects the payload back. Swap that for your predicate logic.

## A note on `is`

`is` is the one **built-in** predicate. Mycelium applies `is` inheritance in-process and **never dispatches it to an external handler** — only predicates like `consumes`/`produces` are mapped to external services. So you cannot write an external "is handler"; register your service for a custom predicate (or `consumes`/`produces`) instead.

## Things and relations, not strings

When something could be a Thing in the model or a string on one, make it a Thing. When a value names
another Thing, relate to it instead of copying its name into a string.

A string is a dead end. Nothing can walk to it, nothing can hang a property off it, no range can judge
it, and no reader can ask what else is true of it. A Thing can be extended by editing the model; a
string can only be extended by editing a service and deploying it — in a platform whose whole point is
that the specifics live in the model rather than in code.

**A value that names a kind is a Thing.** If you would ever want to say something *about* the value —
what it means, what it maps to, who may use it — it is a Thing, not a word.

**A value that names another Thing is a relation.** If the model already holds what the value refers
to, write an edge to it. A name copied into a property cannot be traversed, cannot be checked, and goes
stale the moment the Thing it names is renamed.

**A string is right for a scalar datum about one Thing** that nothing else needs to reason about: a
phone number, an email address, free prose a person wrote. The test is whether anything would ever ask
a question *of* the value. Nobody asks what a phone number means. Names from a standard a service reads
— the building-model type names, for instance — stay as they are; they are that standard's vocabulary,
not the model's.

Two examples in this repository that get it wrong, kept here because they are the shape to recognise:

| Where | What it does | What it costs |
|-------|--------------|---------------|
| `SubmissionFragmentComposer` | Holds the allowed boundary sources as a list in C# and refuses anything else | Adding a way of obtaining a boundary means changing a service and deploying it |
| `HazardAssessment.assessmentSource` | A string naming where an assessment came from, while `DataSource` is a Thing in the same model | Nothing can walk from a hazard to what produced it, and the two can disagree with nothing to notice |

Neither is a reason to rewrite them on sight — fix them when the work is already in that file.

## Lifecycle

```mermaid
sequenceDiagram
    participant M as Mycelium
    participant S as Your service
    M->>S: launch: app --port --myceliumUrl --issuer --audience (Token + SigningKey in the environment)
    S->>M: POST /api/auth/token (skip if Token is set) → { token }
    S->>M: POST /api/mycelium/register (Bearer) → 200
    Note over M,S: relationship with your predicate is created
    M->>S: POST /handle (Bearer mycelium_request JWT) → 200
    M->>S: GET /health (polled) → Healthy
    M->>S: SIGTERM / POST /shutdown
    S->>M: DELETE /api/mycelium/services/{handlerId} (Bearer)
```

## CLI arguments

Mycelium launches your binary with `--key=value` flags. `--port` and `--myceliumUrl` are required; the rest are optional.

| Flag | Required | Meaning |
|------|----------|---------|
| `--port` | ✓ | Port to listen on (1–65535) |
| `--myceliumUrl` | ✓ | Base URL of Mycelium, e.g. `https://localhost:7243` |
| `--issuer` | | JWT issuer to validate against. **Required when `SigningKey` is set** (startup fails otherwise); must match what Mycelium signs |
| `--audience` | | JWT audience to validate against. **Required when `SigningKey` is set** (startup fails otherwise); must match what Mycelium signs |

### Credentials

Read these from configuration or the environment, **never** from the command line. Mycelium sets both
on the environment of the daemon it launches. A command line is readable by every process on the host
and is recorded by anything that logs the line a service was started with.

Launch a process of your own and the same rule applies to what you hand it: set the credential on the
child's environment, not in its arguments. Xylem hands the IFC ingest tool its `Token` that way.

| Setting | Meaning |
|---------|---------|
| `Token` | Pre-minted service JWT for outbound calls; if unset, fetch one from `POST /api/auth/token` |
| `SigningKey` | Base64-encoded HMAC key for validating **inbound** requests. When present, `/handle` and `/shutdown` require auth; when absent, auth is disabled |

## Registration

On startup, obtain a bearer token then register.

1. **Token** — use the `Token` setting if provided, else `POST {myceliumUrl}/api/auth/token` (no body) → `{ "token": "<jwt>" }`.
2. **Register** — `POST {myceliumUrl}/api/mycelium/register` with `Authorization: Bearer <token>` and body:

```json
{
  "handlerId": "<uuid generated at startup>",
  "serviceName": "YourService",
  "endpointUrl": "http://localhost:<port>",
  "startCommand": "endpoint-service",
  "stopEndpoint": "http://localhost:<port>/shutdown",
  "healthEndpoint": "http://localhost:<port>/health"
}
```

A 2xx means you're registered.

## Endpoints to expose

| Method | Path | Auth* | Purpose |
|--------|------|-------|---------|
| POST | `/handle` | ✓ | Process a relationship payload |
| GET | `/health` | — | Liveness probe (Mycelium polls during startup; must answer quickly) |
| GET | `/stats` | — | Service metadata |
| POST | `/shutdown` | ✓ | Graceful shutdown trigger |

\* Auth enforced only when a `SigningKey` was supplied.

**Timing guarantee.** When the trigger relationship arrives inside a `POST /api/model/fragment`
batch, `/handle` is called only after the whole fragment is applied — every Thing, edge, and
property value in the batch is readable, and roll-ups are recomputed. A handler never observes a
half-applied fragment. Multiple handled edges in one fragment are dispatched in creation order.

**`POST /handle`** receives (camelCase JSON):

```json
{
  "relationshipId": "<uuid>",
  "subjectId": "<uuid>",
  "targetId": "<uuid>",
  "subjectName": "BuildingA",
  "targetName": "LandParcel1",
  "properties": { "...": "predicate-specific" }
}
```

Return any 2xx; Mycelium logs non-2xx and continues. A reasonable body:

```json
{ "success": true, "service": "YourService", "relationshipId": "<uuid>", "status": "handled" }
```

**`GET /health`** → `{ "status": "Healthy", "service": "YourService", "requestsProcessed": <n> }`

**`GET /stats`** → `{ "service": "...", "version": "...", "requestsProcessed": <n>, "handlerId": "<uuid>", "myceliumUrl": "..." }`

**`POST /shutdown`** → `{ "message": "..." }`, then exit (deregister first).

## Inbound JWT validation

When a `SigningKey` is supplied, validate the Bearer JWT on `/handle` and `/shutdown`:

1. **Key** = `base64decode(SigningKey)` → use directly as the **HMAC-SHA256** secret.
2. **Algorithm** = HS256.
3. **Claims** — validate `iss` and `aud` against the `--issuer` and `--audience` flags, and `exp`/`nbf`, allowing **30 seconds** clock skew.

Mycelium signs each `/handle` call with a short-lived (5-minute) service JWT carrying `iss`/`aud` and the request's `vos:model_id`; the platform validates this same HS256 JWT before dispatch, so your handler should apply the identical checks. Reject with 401 on any failure.

**Calling back into Mycelium.** If your handler writes back during `/handle` (Facts, Observations, relationships), authenticate those calls with the **inbound** request token, not the startup JWT from `Token` — otherwise a daemon shared by several models writes to whichever model launched it. Handlers built on `MyceliumClientBase` get this for free: add `app.UseMyceliumRequestToken()` after `UseAuthorization()`, and `GetTokenAsync()` prefers the current request's bearer.

## Deregistration & health

- On `SIGINT`/`SIGTERM` and on `POST /shutdown`, send `DELETE {myceliumUrl}/api/mycelium/services/{handlerId}` (Bearer) before exiting.
- Keep `/health` fast and dependency-free — Mycelium uses it to decide a daemon started successfully.

## Authoring checklist

- [ ] Parse the `--key=value` flags; exit with usage if `--port`/`--myceliumUrl` missing
- [ ] Read `Token` and `SigningKey` from configuration or the environment, not from the command line
- [ ] Generate a `handlerId` UUID at startup
- [ ] Get a token (the `Token` setting or `/api/auth/token`) and `POST /api/mycelium/register`
- [ ] Serve `/handle`, `/health`, `/stats`, `/shutdown`
- [ ] Validate the inbound HS256 JWT when a `SigningKey` is set (iss/aud/exp, 30s skew)
- [ ] Add `app.UseMyceliumRequestToken()` so `/handle` callbacks use the request's model token
- [ ] Deregister on shutdown
- [ ] Add tests for arg parsing + JWT validation (see any reference example)
