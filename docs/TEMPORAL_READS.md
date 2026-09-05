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

## Which clock T is measured against

**Model time** — the clock `GET /api/time` serves. An instant you pass to an as-of read is read in
that clock, the timestamps you get back are recorded in it, and it is the clock the platform judges
by when it evaluates a criterion or a range.

Left alone, model time *is* wall time, so the distinction never shows. A deployment that **anchors**
the clock — setting a start instant, a rate, or both — moves the two apart by however much it
chooses, and from then on the difference decides whether a read lands on the history at all. Ask in
wall time against an anchored model and you are asking about an interval nothing was recorded in.

So a client that reasons about time should read the current instant from `/api/time` rather than from
its own clock, and stamp any timestamp of its own from the same place. One clock, one base, whether
or not anything ever anchors it.

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

### What a read answers beyond a property's retention

A temporal read answers with the value that **stood at the instant asked for**. Where the property's
retention cannot reach that instant, the property is **left out of the answer** — not returned
carrying the value it holds now.

| Instant asked for | Answer |
|---|---|
| At or after the property's last write | The current value. It is the value that stood then |
| Earlier, and within what the mode retains | The value in force then |
| Earlier, and beyond what the mode retains | The property is omitted |

So an absent property in an as-of read means **no value can be known for that instant**. It does not
mean the value was null: a property that genuinely held null reports null. The two are different
answers and are reported differently.

With `CurrentOnly`, every instant before the last write is beyond retention — that is what keeping
nothing amounts to when something asks about the past. Ask at or after the last write and the answer
is exact; ask earlier and there is nothing to answer from.

## What a read answers about a Thing or relationship that was not there

A Thing and a relationship each carry the instant the model started holding them, so an as-of read
answers about the graph as it stood rather than the graph as it is now:

| Instant asked for | Answer |
|---|---|
| Before the object joined the model | Left out. `GET /api/things/{id}?timestamp=` answers `404` |
| While it stood | Included, with the values that stood then — see below for where they are |
| At or after it was retracted | Left out — the same answer as before it arrived |

The instant is taken when the object **joins the model**, not when a client composed it, so composing
a relationship and inserting it later dates it from the insertion.

## What a Thing at an instant carries

A Thing or a relationship read at an instant carries values in the two places the live read carries
them, each as of the instant:

| Member | Holds |
|---|---|
| `Properties` | Its own properties, each at the value in force then |
| `InheritedOverrides` | The values it holds for names its sources declare, keyed by source id and nested as the live read nests them, each at the value in force then |

The second is where a value written onto an archetype-declared name lives, so it is where every seeded
value and every value a service writes through the fragment upsert is answered from. A reader that
resolves a live Thing by reading `Properties`, then its override sets, then up the `is` chain reads an
instant the same way: the archetypes' defaults at the instant are the own properties of the archetype
Things in the same snapshot, and the `is` edges live then are in the same answer.

Values in both members are bare, not the `{ typeInfo, value }` envelope the live read uses. A value
that cannot be answered for the instant is left out, under the retention rule above, and so is an
override that did not yet exist then; an override set with nothing left to say is left out whole, and
the source's own value in the same snapshot is what the object held.

## A restart does not move the history

Every instant the model recorded is written durably alongside what it describes — when an object
began, when it was retracted, when each property value took effect. A restart replays that history and
restores those instants, so an as-of read gives the same answer before and after one. Timestamps you
read from the platform are safe to store and ask about again later.

This holds for a run on an anchored clock too: the instants come back in model time, not in the wall
time of whenever the platform was last started.

## Reducing into time buckets

A history read answers *what one property was worth over time*. A different question — *how much
happened per slice of time, across everything of a kind* — is answered by
`POST /api/temporal/aggregate`.

It reads the **live model**, not the tiers: the members are the instances of a type, each placed by
an instant it carries as an ordinary property, written once when its event happened. So the cost
follows the member population rather than the depth of history, and the answer does not shrink when
a property's retention runs out.

```http
POST /api/temporal/aggregate
{"function":"Sum","memberType":"Dispatch","timestampProperty":"left_at","measureProperty":"units",
 "windowSeconds":28800,"bucketSeconds":900}

{"buckets":[12,0,7,…],"firstBucketStart":"2026-07-05T04:00:00+00:00","bucketSeconds":900,"unusableMembers":0}
```

| Field | Meaning |
|---|---|
| `function` | The reduction per bucket: `Min`, `Max`, `Sum`, `Average` or `Count` |
| `memberType` | Only Things that `is` this type, followed through the whole chain. A Thing declared as a type never contributes, only its instances |
| `timestampProperty` | The property holding the instant each member's event happened |
| `measureProperty` | The property reduced per member. `Count` needs none; every other reduction does |
| `windowSeconds` | How far back the window reaches from the model clock's now |
| `bucketSeconds` | How wide each bucket is. The window must be a whole number of them, and at most ten thousand |
| `within` + `withinPredicate` | Only members this container reaches through the named predicate, at any depth |

The window ends at the **model clock's** now — the same clock as every other temporal read on this
page — and each bucket is closed at its end, so an event exactly at now falls in the last bucket and
one exactly at the window start belongs to the window before this. A window equal to one bucket is a
trailing-window scalar: the question a tile asks, answered by the same code as the series it sits
above. An answer holds at most ten thousand values, so a caller reaching further back widens its
buckets rather than asking for a reply nobody can read.

A question the platform cannot run is refused with `400` naming what is wrong, rather than answered
with an empty series that would read as "nothing happened". Members carrying no readable instant or
measure do not fail the request; they are counted in `unusableMembers`, so a reading of zero because
nobody stamped the instant is distinguishable from a reading of zero because nothing happened.

## See also

- [`TRIBUTARY.md`](TRIBUTARY.md) — the outbound fetcher that ingests readings as observations into
  these tiers.
- [`SERVICES.md`](SERVICES.md) — bulk historical backfill (Sediment) that writes sealed
  Sapwood buckets directly.
