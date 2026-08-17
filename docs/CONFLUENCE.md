# Confluence

Confluence resolves a site against the outside world. Given a Site, it works out which data
sources cover that site, calls each one through Tributary with the site's coordinates, and
reports what did and did not resolve. The name is where tributaries meet: many Tributary
calls converging on one Site.

It runs **before** the analysis pipeline, never inside it. Discovery populates the Site;
the pipeline reads what discovery wrote. That keeps the pipeline a pure calculation graph
with no network dependency, so a planner adjusting an assumption reruns the analysis
instantly and without touching a public data portal again.

> Today Confluence answers *which sources cover this site*. Calling them, the
> partial-failure report, and spawning the pipeline are the next two slices.

## Coverage is edges, not a word

A source declares where it applies by relating to a **Place** Thing, and a site relates to
the Place it sits in. Places nest, so a source covering a region covers every site within
it without naming any of them:

```mermaid
flowchart LR
  Site["<b>WillowBend</b> (Site)"]
  PT["<b>Portugal</b> (Place)"]
  EU["<b>Europe</b> (Place)"]
  Earth["<b>Earth</b> (Place)"]
  Flood["<b>NationalFloodPortal</b><br/>(DataSource)"]
  Meteo["<b>OpenMeteo</b><br/>(DataSource)"]
  FloodEp["<b>NationalFloodPortal endpoint</b><br/>(Tributary registration)"]
  MeteoEp["<b>OpenMeteo endpoint</b><br/>(Tributary registration)"]

  Site -->|isIn| PT -->|isIn| EU -->|isIn| Earth
  Flood -->|covers| PT
  Meteo -->|covers| Earth
  Flood -->|resolvedBy| FloodEp
  Meteo -->|resolvedBy| MeteoEp
```

Both sources above cover `WillowBend`: one directly, one through the nesting.

**Why not a `coverage` string.** The failure this path exists to prevent is a source that
does cover the site being silently skipped because a country was written two ways —
`Portugal` against `PT`, or a difference in case. Matching strings is what causes that;
walking edges makes it impossible rather than merely reported. Adding a country becomes a
model edit with no deployment, and a reader can ask what else is true of a Place. This is
the repository's *things and relations, not strings* rule applied to the one place where
getting it wrong is invisible: a source that is never selected cannot appear in the
unresolved list either, because nothing knew to look for it.

## The three predicates

| Predicate | Reads | Why |
|---|---|---|
| `isIn` | `Site isIn Place`, `Place isIn Place` | Where the site is, and what contains that. Walked to any depth, so nesting can be as deep as a model wants. |
| `covers` | `DataSource covers Place` | Where a source applies. Several `covers` edges are fine; the source is still selected once. |
| `resolvedBy` | `DataSource resolvedBy Endpoint` | Which Tributary registration a call goes through. A relation, not a copied name, so renaming the registration cannot strand the source. |

A `DataSource` with no `resolvedBy` edge is **left out** rather than reported as a failure.
It is not a source that failed — it was never callable, and listing it as unresolved would
blame a provider for a gap in the model.

## Reading the model

One scoped snapshot answers everything: the site's Places, then every source whose coverage
reaches one of them, then those sources' registrations. Traverse rules compose over the set
built so far, so the walk must ask for `isIn` **first** — asking for the incoming `covers`
edges before the Places are in the set finds nothing. That is the same ordering trap the
Tributary endpoint kinds hit; see [`TRIBUTARY.md`](TRIBUTARY.md).

A failed read returns **502**, never an empty list. An unreachable gateway must not read as
"no source covers this site" — the two answers look identical to a caller and only one of
them is true.

## Pointers

- [`TRIBUTARY.md`](TRIBUTARY.md) — the fetcher Confluence calls, its per-call address
  parameters, and how a reshape expression turns a response into an observation on the Site.
- [`LAND_INTAKE.md`](LAND_INTAKE.md) — the intake design this serves, and the model the
  Site, Parcel and DataSource archetypes sit in.
