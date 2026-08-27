# Forage

Forage resolves a site against the outside world. Given a Site, it works out which data
sources cover that site, calls each one through Tributary with the values the model holds
for the call, and reports what did and did not resolve. The name is what an organism does when it goes out to
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
cannot be submitted". The hazard portal covers the root too — it is global — but its address needs a
Place carrying its administrative division code (`hazardPortalDivision`), which a project declares in
its own model and relates its sites into. A site reaching no such Place has the portal's calls
refused before the provider is contacted and reported with the unfilled placeholder named: the model
gap said out loud, where hiding the source would leave a quietly short report.

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
| `resolvesOnto` | `DataSource resolvesOnto archetype` | What a source's readings are about, where that is not the site itself. A source naming an archetype is called once per Thing the site `has` of it, with that Thing as the call's subject — the hazard portal grades one assessment per call. |
| `studies` | `SiteStudy studies Site` | The study the analysis computes. Read incoming, because the edge runs from the study to the site. |
| `has`, `is` | `Connection has Service`, `Service is prototype`, `Site has HazardAssessment` | Which service a connection dispatches, the prototype an analysis edge points at, and the Things a per-subject source is called about. |
| `assesses` | `HazardAssessment assesses HazardType` | What an assessment is about. The type Thing carries the portal's code for it, and a per-assessment call is addressed with what its subject reaches — a second per-subject source whose vocabulary hangs off a different predicate adds that predicate here. |

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

## What starts a run

**A site entering a state, not a call.** The model declares a range on the `Site` archetype,
`SiteAwaitingDiscovery` — coordinates known, and nothing has written onto the site yet — and a
connection bound to this service watches it. A site entering that state makes the platform write a
durable record-edge from the site to the connection and dispatch it. Nothing in either repository
calls Forage, and nothing has to: **what started a run is a fact in the model afterwards**, which a
call over HTTP would have left only in a log.

| | How it happens |
|---|---|
| A run starts | A site's coordinates are written and no source has yet observed it |
| A run does not start again | The first source to ingest relates its registration to the site through `observed`, the count moves off nought, and the site leaves the state on its own |
| A run that reached nothing retries | No source resolved means no `observed` edge, so the site stays in the state and the next load dispatches again — bounded by the platform's oscillation guard |
| A surveyed site | Enters the same state and is discovered the same way; nothing here is particular to a submission |

The range and the connection are declared in the platform repository — the range beside the `Site`
archetype in `land-intake.template.json`, the connection beside the sources in
`open-data-sources.template.json` (platform Task #6771). A deployment that never fetches reads
neither file and runs no discovery service.

## The run

`POST /handle { subjectId }` **accepts** a run and answers `202` at once; the fetching happens after.
The body is whatever the platform posts for a dispatch — the record-edge's own fields — and the
subject is read from it through the classifier every dispatched service shares, so this service
declares no request shape of its own.

**The answer says accepted, not done.** A run fetches every covering source before it could say what
it found, and the platform gives a dispatch **15 seconds**; with tens of shared sources, fetched
`--maxConcurrentSources` at a time and each allowed `--sourceTimeoutSeconds`, a run outlasts that call
routinely. Judged by the call, finished work would be recorded Failed and driven again. So what closes
the dispatch is the model: the connection names `SiteDiscovered` as its `done_when`, the record stays
in flight until the site shows a source has written onto it, and `done_within` presumes dead a run
nobody will finish. Completion is *observed* rather than *announced* because nothing exposes a route
for a service to announce one.

**A coverage read that fails writes nothing.** It cannot refuse in the answer any more — one has
already been given — so it says so by leaving no trace: no fetch, no analysis started, and the site
still in the state that dispatched it, which is what drives the run again. An unreachable gateway must
never be mistaken for "no source covers this site".

What a run found goes to the log, per source and with the provider's own words. Persisting it where a
planner can read it afterwards is platform Task #6779, which needs it for a different reason.

A run's report, as the log records it:

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

**A call is addressed from its subject outward.** Whatever the call's subject carries — coordinates,
elevation, climate zone — is passed as its address parameters, and behind it, layered so the most
specific holder of a name decides it: the subject's own values first, then (for a per-subject call)
the values of the Things it reaches by its own outgoing edges, then the site's, then each Place the
site is in, nearest first. A source's address takes only the placeholders it names and the fetcher
ignores the rest, so one set of values serves a source wanting coordinates, one wanting a division
code, and one wanting neither, with no per-source arrangement here. Inherited values are left out — a
value from an archetype is a default for a *kind* of site, and calling a provider with a default
location would return a confident reading about somewhere else — and so are the double-underscored
marks readers find Things by, and any name that Things standing at the same distance disagree on,
because relationship order is undefined and taking either would address different calls on different
runs.

**A source that declares what it resolves onto is called once per Thing, not once per site.** The
hazard portal grades one assessment per call: its `resolvesOnto` edge names the `HazardAssessment`
archetype, so a run calls it once for each assessment the site `has`, with that assessment as the
call's subject — the reading lands on the assessment it grades, and the division and hazard codes
arrive from the Place and from the type Thing the assessment `assesses`. A declared source on a site
holding nothing of its archetype has nothing to fetch: that is logged and is not a failure, the same
rule a site with no study is reported under. A failed per-subject call names its subject in the
report, because a portal answering for five assessments and not the sixth must not read as a source
that failed outright.

**Bounds.** Calls run concurrently up to `--maxConcurrentSources`, held across every call rather
than per source, so a site covered by many sources — or a source called once per assessment — cannot
open a burst of connections that reads as abuse. Any one source is bounded by
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

## Resolving the fetched words

**A fetched word becomes the edge the model declares, inside the run that fetched it (#6809).** Some
vocabularies take their word from a fetch — a site's climate class, an assessment's hazard grade —
and a fetch writes property values, never edges. So after the fetches, the run resolves each written
word against the vocabulary the model declares and relates the subject to the member it names.

**Everything is read from the declaration** (platform User Story 6773): the vocabulary archetype
carries `__IsDiscoveredVocabularyArchetype` and names, in `resolvedFromProperty`, the property its
word arrives under; an archetype-level edge — `Site classifiedAs ClimateZone`,
`HazardAssessment gradedAs HazardLevel` — names the Thing the word is written onto and the predicate
the resolved edge is written through. Nothing here names a vocabulary, an archetype, a predicate or a
property of any model, so a project adding a vocabulary edits its model and deploys nothing.

**The words come from the fetch responses, not from reading the model back.** An observation is
accepted into a queue and applied by the drainer after the write returns, so a read straight after
the call races it; the fetching service reports what each subject call wrote (`written`, see
[`TRIBUTARY.md`](TRIBUTARY.md)), and that report is what is resolved.

**A changed word moves the edge.** An edge already pointing at the named member is left alone; one
pointing elsewhere is removed before its replacement is written, so a re-graded assessment never
carries two levels. A stale edge that will not go blocks its replacement — a reader must never meet
two answers — and the next run resolves again.

**A word the vocabulary does not hold writes no edge and is reported**, naming the source, the
subject, the property, the word and the vocabulary. Writing it would invent a member the scheme does
not hold; refusing the fetch would turn a provider's odd answer into an outage. The word stays on the
subject's series as the record of what the source answered — for these vocabularies the series is the
provenance and the edge is the conclusion, which is why no retired-word rule applies to them. A word
that matches a member only up to case, and a member name two Things carry, are reported the same way
rather than guessed at.

**A failed declaration read resolves nothing and says so.** The observations are already written and
the site has left the state that dispatches runs, so nothing retries by itself; the words stay words
until something runs discovery again.

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
