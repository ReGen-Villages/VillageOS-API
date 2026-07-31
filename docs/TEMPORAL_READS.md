# Temporal Reads

VillageOS is a temporal knowledge graph: every property value and relationship carries its
history, and you can query state "as of now" or "as of a past time." This page is the canonical
reference for *how* those reads are served — the tiered time-series store behind history queries,
how data is addressed across tiers, and why current-state reads stay fast.

## Two read shapes, two homes

- **"As of now"** — the current value of a property, the live set of relationships — is answered
  entirely from the in-memory model. It never touches the time-series tiers.
- **"As of time T"**, and **"how did X change between T1 and T2"** — historical and temporal
  reads — are served from **Rings**, the time-series store.

This split is what keeps high-frequency, long-lived observation streams sustainable: the
in-memory footprint stays bounded by the size of the graph, not by the length of its history.

## Derived-state history is the exception

A Thing's derived states (the ranges whose criteria hold) are computed, never stored — there is no
state Fact and no state series in Rings. Their *history* is therefore served from a third place: the
reactive engine's in-memory tracker, which records every change point as it happens.

- `GET /api/things/{id}/state-transitions` — change points: states entered and exited, plus the
  triggering property write.
- `GET /api/things/{id}/states/{stateName}/occurrences` — intervals the Thing held one state.

Both responses carry a `Coverage` block. While `Source` is `in-memory` the history only reaches back
to when the engine loaded the model and is lost on restart, so absence of a transition is not
evidence the state never held — read `Coverage.From` before drawing that conclusion. Durable
reconstruction (replaying criteria against Rings) will return the same contract with
`Source: reconstructed` over a wider window, so callers need no change.

## Rings: the tiered time-series store

Rings stores samples keyed by `(entity, property, time-bucket)`. A bucket becomes a sealed,
immutable file once it rotates — cheap to compress, cache, and tier. There are three tiers, named
for a tree's layers from the outside in:

| Tier | Medium | Holds | Read latency |
|------|--------|-------|--------------|
| **Canopy** | RAM | the most recent buckets | sub-millisecond |
| **Sapwood** | local SSD (sealed bucket files) | recent weeks–months | 1–10 ms |
| **Heartwood** | cold archive volume (pluggable backend) | long-term | 100 ms – seconds |

Buckets age outward — Canopy → Sapwood → Heartwood — driven by retention policy. Heartwood is
optional; a small deployment can live entirely in Canopy + Sapwood.

Both write paths land in the same store. Sensor **observations** (`POST …/observations`) and
Fact-path **property changes** (asserting or retracting a value through the API) are both
projected into Rings, each point provenance-tagged with the path it came from. So
`GET …/properties/{p}/history` returns one unified timeline regardless of how each point was
written, and `?timestamp=` as-of reads see both.

## Access is key-addressed — there are no stubs

A read never holds a pointer to "where the data went." Every tier stores its data under the same
`(entity, property)` key, and each can cheaply report the time span it currently holds for that
series — its *coverage*. A temporal read:

1. recomputes the `(entity, property)` key from the query;
2. asks each tier's coverage whether it can intersect the requested time range, skipping the ones
   that can't;
3. reads the tiers that can; and
4. merges the results, de-duplicating on `(observed-time, value)`.

When a bucket ages from one tier to the next, the outer tier keeps **nothing** pointing to it —
the key *is* the address, and the reader re-derives location on every read.

This is a deliberate choice over a paging/swap model that would keep an in-memory **stub** per
series pointing at the tier its data moved to. Stubs are avoided because:

- a stub per `(entity, property)` is memory that grows with the number of distinct series and
  never shrinks — the opposite of what tiering is for;
- the key is already the address, so a pointer would be redundant;
- a single series' history lives across several tiers at once (recent in Canopy, older in
  Sapwood, oldest in Heartwood), so there is no one "moved-to" tier to point at — only a union
  read across the intersecting tiers is correct;
- data moves continually (compaction, age-out, cache re-warm on restart, late or out-of-order
  arrivals), so coverage is derived from what is actually stored on each read — reads stay correct
  with no bookkeeping to maintain.

The one thing a stub would buy — knowing where the data is without asking the tier — is provided
instead by the per-tier coverage check: in-memory for Canopy, a small bucket-range index for the
on-disk tiers.

## Retention is per property

How much history a property keeps — and therefore how far back a temporal read can see it — is set
per property by its **PropertyMode**:

| PropertyMode | Kept in Rings |
|--------------|---------------|
| **CurrentOnly** | nothing (current value only) |
| **RingBuffer** | the most recent N samples |
| **Sampled** | a decimated trend (every Kth sample, or rate-bucketed) |
| **FullHistory** | every sample, tiered Canopy → Sapwood → Heartwood |

`CurrentOnly` is the default for streamed observations; `FullHistory` is reserved for properties
whose audit value justifies the cost. A background compactor enforces each mode on sealed buckets,
so storage growth is bounded at the source.

## See also

- [`TRIBUTARY.md`](TRIBUTARY.md) — the outbound fetcher that ingests readings as observations into
  these tiers.
- [`SERVICES.md`](SERVICES.md) — bulk historical backfill (Sediment) that writes sealed
  Sapwood buckets directly.
