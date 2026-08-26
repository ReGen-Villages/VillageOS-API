# Forage

Forage resolves a site against the outside world. Given a Site, it works out which data
sources cover that site, calls each one through Tributary with the site's coordinates, and
reports what did and did not resolve. The name is what an organism does when it goes out to
find what its surroundings hold and brings it back — which is the whole of this service, and
what `Mycelium` is named for doing underground.

It runs **before** the analysis, never inside it. Discovery populates the Site; the compute
services read what discovery wrote. That keeps the compute free of any network dependency, so
a planner adjusting an assumption sees the balances move without touching a public data portal
again.

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

**Where that graph comes from.** The `Place` archetype, the three predicates and the sources every
project shares are seed data: `open-data-sources.template.json` in the platform repository, read
alongside the archetype set (platform User Story #6750). A registration lives in the project's own
model, so a source every project uses belongs in the seed every project is created from — see
[`DELTA.md`](DELTA.md#which-model-a-registration-lives-in). **A site's own `isIn` edge is written by
its producer, not inherited from the archetype.** The submission producer writes one per site, to the
Place carrying `__IsRootPlace` through the predicate carrying `__IsPlaceNestingPredicate` — both found
by mark, because a producer naming `Earth` would relate nothing, and say nothing, in a model that
called its root something else (Bug #6752).

**The root is enough for a source that covers everything, and no more is claimed.** A submitted site
is not related to a country or a region: `country` on a submission is optional and stays text, it
names an open set nobody can enumerate, and the vocabulary pattern used elsewhere here *refuses* a
term the model does not hold — which would turn "we have not declared your country" into "your land
cannot be submitted". The first source whose coverage is narrower than the whole world is what should
force that design, and the hazard portal needs a Place carrying its administrative division code, so
that is where it belongs.

**Why not a `coverage` string.** The failure this path exists to prevent is a source that
does cover the site being silently skipped because a country was written two ways —
`Portugal` against `PT`, or a difference in case. Matching strings is what causes that;
walking edges makes it impossible rather than merely reported. Adding a country becomes a
model edit with no deployment, and a reader can ask what else is true of a Place. This is
the repository's *things and relations, not strings* rule applied to the one place where
getting it wrong is invisible: a source that is never selected cannot appear in the
unresolved list either, because nothing knew to look for it.

## The predicates it reads

| Predicate | Reads | Why |
|---|---|---|
| `isIn` | `Site isIn Place`, `Place isIn Place` | Where the site is, and what contains that. Walked to any depth, so nesting can be as deep as a model wants. |
| `covers` | `DataSource covers Place` | Where a source applies. Several `covers` edges are fine; the source is still selected once. |
| `resolvedBy` | `DataSource resolvedBy Endpoint` | Which Tributary registration a call goes through. A relation, not a copied name, so renaming the registration cannot strand the source. |
| `studies` | `SiteStudy studies Site` | The study the analysis computes. Read incoming, because the edge runs from the study to the site. |
| `has`, `is` | `Connection has Service`, `Service is prototype` | Which service a connection dispatches, and the prototype an analysis edge points at. |

Connections are **not** read by name. Every connection a site analysis starts `is` an archetype
carrying `__IsSiteAnalysisConnectionArchetype`, and they are asked for by that mark, model-wide —
the study is not related to them yet, because relating it is what the read is for. Adding a fourth
balance is a mark in the model, not a change here.

A `DataSource` with no `resolvedBy` edge is **left out** rather than reported as a failure.
It is not a source that failed — it was never callable, and listing it as unresolved would
blame a provider for a gap in the model. A `Site` with no study, and a model marking no analysis
connection, are treated the same way: the run reports it in `analysis.reason` and logs nothing,
where a service that could not be started logs an error — nothing was ever going to run, so there
is no outage for an operator to look into.

## Reading the model

One scoped snapshot answers everything: the site's Places, then every source whose coverage
reaches one of them, then those sources' registrations. Traverse rules compose over the set
built so far, so the walk must ask for `isIn` **first** — asking for the incoming `covers`
edges before the Places are in the set finds nothing. That is the same ordering trap the
Tributary endpoint kinds hit; see [`TRIBUTARY.md`](TRIBUTARY.md).

A failed read returns **502**, never an empty list. An unreachable gateway must not read as
"no source covers this site" — the two answers look identical to a caller and only one of
them is true.

## The run

`POST /handle { siteId }` resolves the site against every covering source and answers with both
halves:

```jsonc
{ "siteId": "…",
  "resolved":   ["OpenMeteo", "Copernicus"],
  "unresolved": [ { "source": "NationalFloodPortal",
                    "reason": "503: portal is down for maintenance" } ],
  "analysis":   { "started": true, "reason": null } }
```

**Partial failure is normal and is tolerated.** Public data portals go down, and an intake that
aborted because one provider was unavailable would be abandoned. A source that fails leaves its
value undiscovered, does not stop the others, and appears in `unresolved` with a reason — carrying
the provider's own words where there are any, because that is the most useful thing a planner can
be told about why a value is missing. Reporting what did *not* resolve matters as much as reporting
what did: a quietly short list is the failure of the tool being replaced.

**The site's own values go to every source.** Whatever the Site carries — coordinates, elevation,
climate zone — is passed as the address parameters of every call. A source's address takes only the
placeholders it names and the fetcher ignores the rest, so one set of values serves a source wanting
coordinates, one wanting elevation, and one wanting neither, with no per-source arrangement here.
Inherited values are left out: a value from an archetype is a default for a *kind* of site, and
calling a provider with a default location would return a confident reading about somewhere else.

**Bounds.** Sources resolve concurrently up to `--maxConcurrentSources`, so a site covered by many
sources cannot open a burst of connections that reads as abuse. Any one source is bounded by
`--sourceTimeoutSeconds`: a provider that accepts the connection and then goes quiet is more common
than one that refuses outright, and it must not hold up the run. A run cancelled by its caller is
never reported as a timeout — that would put a fabricated outage in front of a planner.

**Fetching is not done here.** Forage asks Mycelium to forward each call to the endpoint service
named by `--fetcherSubdomain`, which resolves the registration, fills the address placeholders,
reshapes the response and writes the observation onto the Site. **Each call names the site as its
subject**, so the reading lands on the site the run is for rather than on whatever entity the shared
registration's expression names — see
[`TRIBUTARY.md`](TRIBUTARY.md#naming-the-subject-a-call-is-about). Which service fetches is
configuration rather than a name in code, so a deployment can point it elsewhere without editing
this service.

**Every source that resolved stays reachable from the site.** The fetch relates the registration to
the site through `observed`, so a value on the site leads back to the registration and from there to
the `DataSource` along the `resolvedBy` edge this service already reads. Nothing here writes that edge;
it is the ingest's, and it is written once per registration per site however often discovery runs —
see [`TRIBUTARY.md`](TRIBUTARY.md#which-registration-wrote-a-value). A source that did not resolve
never reaches the ingest, so it leaves no edge suggesting it did.

## Starting the analysis

**When the run finishes, Forage relates the site's study to each compute service** — one
`SiteStudy -connection-> prototype` edge per marked connection. A connection bound to a service is a
handled predicate, so creating that edge is what dispatches it; no compute service is called from
here. The model carries the trigger, which means there is one way to start an analysis rather than
two to keep in step, and what started a given analysis is answerable from the model afterwards rather
than only from a log.

**The subject is the study, never the site.** A compute service reads its inputs off the study, so an
edge naming the site would dispatch the service against a Thing carrying none of them.

**The edge is written once.** Dispatch makes the service compute and start watching the study, so
every later change to an input recomputes on its own. Discovery does not have to run again for a
balance to stay current.

**It starts whatever mixture resolved, including none.** A site whose sources were all unavailable is
exactly the case a planner needs an answer about; an analysis that only ran when everything succeeded
would go quiet precisely when something had gone wrong. The balances report against what discovery
left them, and `unresolved` says what is missing and why.

**Discovery runs before the analysis, never inside it.** That is what keeps the compute free of any
network dependency: a planner adjusting an assumption sees the balances move without touching a
public data portal again, because the values are already on the Site. It matters more here than it
would under a graph run once per request, because a reactive chain re-fires on every input change.

**A failure to start does not lose the report.** The observations are written by the time the
analysis is asked for, so a site with no study, a model marking no analysis connection, or a refused
relationship write are each reported in `analysis.reason` with the discovery report intact beside
them. A write that fails for one connection names that connection.

## Pointers

- [`TRIBUTARY.md`](TRIBUTARY.md) — the fetcher Forage calls, its per-call address
  parameters, and how a reshape expression turns a response into an observation on the Site.
- [`LAND_INTAKE.md`](LAND_INTAKE.md) — the intake design this serves, and the model the
  Site, Parcel and DataSource archetypes sit in.
