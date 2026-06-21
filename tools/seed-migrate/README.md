# seed-migrate

Migrates a VillageOS **seed file** from the old fused service model to the model
where ordinary `is` relationships to archetype Things carry everything
(Feature #5615).

## Why this exists

The old design put a service's launch information **directly on the Thing that
selects it**:

- A handled predicate (e.g. `consumes`) carried `ExecutablePath`, `ServicePort`,
  `ServiceArgs`, `onLoad`.
- An `EndpointService` (e.g. `Echo`) carried `ExecutablePath`, `ServicePort`,
  `Subdomain`.

That duplicated the executable across every connection sharing a binary (both
`consumes` and `produces` are the Metabolism microservice) and fused two ideas —
*how a request is routed* and *what process serves it* — onto one Thing.

## The new model

Everything is expressed with the built-in `is` relation and inheritance —
the same machinery as `Echo is EndpointService` today. Nothing is hardcoded in
the platform: Mycelium is told the archetype Thing names via config
(`PrototypeConnectionThingName` / `PrototypeServiceThingName`) and finds
connections/services as instances by transitive `is`.

```text
<connection>  --is-->  Connection         (archetype: trigger, …)
<connection>  --has-->  <service>          (generic `has`; target is a Service)
<service>     --is-->  <shared prototype>  (e.g. Metabolism prototype)
<shared proto>--is-->  Service             (archetype: ExecutablePath, ServicePort,
                                             ServiceArgs, AutoStart, RunMode)
```

- **`Connection`** archetype — the selector schema (`trigger` = `graph`/`http`).
  A connection instance is anything `is Connection`; HTTP ones also carry
  `Subdomain`.
- **`Service`** archetype — the process schema (`ExecutablePath`, `ServicePort`,
  `ServiceArgs`, `AutoStart`, `RunMode`). A service is anything transitively
  `is Service`.
- **Shared prototype** — a `Service` that holds one binary's `ExecutablePath`;
  concrete services `is` it and override only per-instance values (`ServicePort`,
  `ServiceArgs`, `AutoStart`). `consumes` and `produces` share one `Metabolism
  prototype` but bind two distinct services (two processes).
- A connection binds its service through the **generic `has`** relation; a reader
  identifies the service as the `has`-target that is a `Service`, never by a
  predicate name. (Domain `has` edges to non-Service targets are ignored.)

There are **no `__Is*` flags** — membership via `is` is the only marker.

## Usage

```sh
node migrate.js <seed.json>            # print migrated seed to stdout (dry run)
node migrate.js <seed.json> --write    # rewrite the file in place
node migrate.js <seed.json> --check    # exit 1 if the seed still needs migrating (CI gate)
```

A one-line summary (`{connections, services, prototypes, skipped}`) is printed
to stderr. The transform is **idempotent** (after migration the old service
types are gone, so a re-run finds nothing) and **deterministic**: new Thing and
relationship ids are derived (UUIDv5) from source ids, so re-runs never create
duplicates.

## What it does to each service

You do **not** need to hand-edit a seed — run `migrate.js --write`. For
reference, using `consumes` (a Metabolism predicate):

Before:

```jsonc
// consumes — selector AND launch info fused together
{ "Name": "consumes", "Properties": {
    "ExecutablePath": { "value": ".../Metabolism.dll" },
    "ServicePort":    { "value": 7102 },
    "ServiceArgs":    { "value": "--mode=consumes" },
    "onLoad":         { "value": true } } }
// consumes --is--> Handled Predicate
```

After:

```jsonc
{ "Name": "consumes", "Properties": { "trigger": { "value": "graph" } } }
{ "Name": "consumes service", "Properties": {
    "ServicePort": { "value": 7102 },
    "ServiceArgs": { "value": "--mode=consumes" },
    "AutoStart":   { "value": true } } }
{ "Name": "Metabolism prototype", "Properties": {
    "ExecutablePath": { "value": ".../Metabolism.dll" } } }

// relationships:
//   consumes         --is-->  Connection
//   consumes         --has--> consumes service
//   consumes service --is-->  Metabolism prototype   (inherits ExecutablePath)
//   Metabolism prototype --is--> Service             (inherits RunMode, etc.)
//   (consumes --is--> Handled Predicate is removed)
```

In words:

1. Launch props are lifted off the connection.
2. A **service** Thing is created carrying only per-instance overrides
   (`ServicePort`, `ServiceArgs`, and `AutoStart` — the renamed `onLoad`).
3. A **shared prototype** Service is created (or reused) per distinct
   `ExecutablePath`, holding the binary; connections sharing a binary share it.
4. Relationships are added: `connection is Connection`, `connection has service`,
   `service is <prototype>`, `<prototype> is Service`.
5. The connection gains a `trigger` (`http` if it had a `Subdomain`, else
   `graph`); `Subdomain` stays on the connection as its selector.
6. The superseded `is EndpointService` / `is Handled Predicate` edges and those
   old type Things are removed.

### Not migrated

- The **`is`** predicate and any handled predicate with no `ExecutablePath` —
  these run in-process, not as a daemon (`skipped` in the summary).

## Limitations

- `ContractId` and `TokenScope` are **not** populated — the old seed format
  didn't carry them (token scope lived in broker code). Set them on the `Service`
  archetype / a prototype after migration; see Feature #5615 phases 3–4.
- Resolves inherited launch props (own + `InheritedProperties`); review the
  output if your seed uses an unusual inheritance arrangement.
