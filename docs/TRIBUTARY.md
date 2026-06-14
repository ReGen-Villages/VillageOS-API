# Tributary

Tributary is VillageOS's generic outbound HTTP fetcher. Given an endpoint registered
in Mycelium, it resolves that endpoint's effective properties, performs the HTTP call
(with optional auth and pagination), optionally reshapes the response with a JSONata
expression, and ingests the result as observation Things related to the endpoint.

It is deliberately **source-agnostic** — there is no per-API code. A specific source
(ArcGIS/ESRI, an OAuth2 REST API, a plain JSON endpoint) is expressed entirely as an
endpoint *template* plus a registration, never as a branch in Tributary. The
token-exchange and offset-paging *mechanics* live in `MICROSERVICES.md` section 14.1;
this page covers the model the service sits on and the boundary it respects.

## The endpoint-template graph

Endpoints are not free-form. Delta provisions a **single-rooted template hierarchy**
into Mycelium at boot and validates every registration against it (Feature #5465; see
the *Delta* service docs and `MICROSERVICES.md`). The shape:

- Templates are Things; inheritance is expressed model-natively as `is` relationships
  (`EsriEndpoint is Endpoint`), never a scalar field.
- The graph is single-rooted (`Endpoint`), acyclic, and closed. A registration nominates
  a template via an `is` relationship (or defaults to the root); its admissible
  properties are the **union of property keys along that template's chain**
  (`AllowedKeys`).
- A property's effective value is **closest-ancestor-wins**: the nearest template in the
  chain that declares a non-blank value for the key. A blank value is *structural* — it
  makes the key admissible without supplying an inherited default.
- Tributary never walks the template graph itself. It reads Mycelium's
  **effective-properties** for the endpoint Thing (Mycelium merges the `is`-chain) and
  resolves each property by suffix-aware name match (`EffectivePropertyResolver`, so a
  Mycelium key like `Esri.itemsPath` matches a lookup for `itemsPath`).

## The field taxonomy

Every endpoint property falls into one of four roles. The role says who supplies the
value and whether it is expected to be overridden.

| Role | Meaning | Examples |
|------|---------|----------|
| **required-structural** | A structural key (declared blank on a template) that a registration MUST fill. Admissible via `AllowedKeys`; rejected if missing at use. | `url`; `tokenUrl`, `tokenRequest` (when minting a token) |
| **canonical-default** | A value a *child* template fixes to define a source type's identity — not normally overridden per registration. | `tokenPath`, `expiryPath`, `expiryUnit`; `offsetParam`, `pageSizeParam`, `hasMorePath`, `itemsPath`; a child's `authKind` / `pagingKind` |
| **sensible-default** | Has a built-in fallback (in code or a generic template default); commonly overridden per endpoint. | `httpMethod` (GET), `requestContentType` (application/json), `timeout` (30s), `tokenParam` (token), `responseTransform` ($) |
| **optional** | May be absent entirely; the feature is simply off. | `headers`, `queryParams`, `token` (pre-minted), `tokenHeader` / `tokenScheme`, `pageSize` |

`authKind` and `pagingKind` are *sensible-default* on the root `Endpoint` (unset → `none`,
i.e. off) and become *canonical-default* on a source child that selects a mode
(`tokenExchange` / `offset`).

The `EsriEndpoint` template is the worked example: it restates only the keys it narrows
(`httpMethod=POST`, form-encoded `requestContentType`), selects `authKind=tokenExchange`
and `pagingKind=offset`, and fixes the ArcGIS field names as canonical-defaults
(`tokenPath=token`, `expiryUnit=epochMillis`, `hasMorePath=exceededTransferLimit`,
`itemsPath=features`, …). A registration then supplies only the required-structural
blanks (`url`, `tokenUrl`, `tokenRequest`). The full template JSON is in
`MICROSERVICES.md` section 14.1.

## Fetch-and-shape, not derive — the Metabolism boundary

Tributary's contract is **fetch-and-shape**:

1. **Fetch** — one outbound HTTP call (auth + pagination as configured), aggregating any
   pages into a single body.
2. **Shape** — an optional JSONata `responseTransform` projects the response into the
   observation shape (`[{ name, properties }]`). JSONata here is *structural* — selecting,
   renaming, and restructuring fields — not a place to compute new domain quantities.
3. **Ingest** — each shaped item becomes an observation Thing related to the endpoint via
   an `observed` relationship.

Tributary keeps **no state about the data** and computes **no derived values**. Anything
time-evolving or calculated — simulations, rates, accumulations, consumes/produces
dynamics — is **Metabolism's** job: a stateful daemon running persistent loops over
relationships (see `METABOLISM.md`). The dividing line: if it can be expressed as "fetch
this URL and rename its fields," it is Tributary; if it requires remembering prior values
or computing over time, it is Metabolism. That is why Tributary is stateless and
idempotent per call, and why the JSONata step is constrained to reshaping — derived
calculation deliberately lives on the other side of the boundary.

## Pointers

- `MICROSERVICES.md` section 14.1 — token-exchange auth + offset paging mechanics and the
  canonical `EsriEndpoint` template.
- *Delta* service docs (DevOps wiki) — how the template catalog is provisioned and how
  registrations are validated against it.
- `METABOLISM.md` — the derived-calculation engine on the other side of the
  fetch-and-shape boundary.
- `TEST-STATE.md` — Tributary coverage and the `WebApplicationFactory` test patterns.
