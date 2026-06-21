# seed-migrate

Migrates a VillageOS **seed file** from the old fused service model to the
`Connection → hasHandler → Handler → is → PrototypeHandler` model
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

The new model separates them:

| Concept | Flag | Holds |
| --- | --- | --- |
| **Connection** | `__IsConnection` | the selector: `trigger` (`graph`/`http`) and, for HTTP, `Subdomain` |
| **Handler** | `__IsHandler` | per-instance overrides only: `ServicePort`, `ServiceArgs`, `AutoStart` |
| **PrototypeHandler** | `__IsPrototypeHandler` | the shared definition: `ExecutablePath`, `RunMode` (and later `ContractId`, `TokenScope`) |

A `Connection` binds its `Handler` through the **generic `has`** relation; the
meaning lives on the **target**, so a reader finds a connection's handler as the
`has`-target flagged `__IsHandler` — it never matches a predicate name. A
`Handler` inherits the shared definition from its `PrototypeHandler` via the
ordinary `is` predicate, so the executable is defined once. Both links use
generic relations (`has`, `is`); nothing introduces a dedicated predicate.

## Usage

```sh
node migrate.js <seed.json>            # print migrated seed to stdout (dry run)
node migrate.js <seed.json> --write    # rewrite the file in place
node migrate.js <seed.json> --check    # exit 1 if the seed still needs migrating (CI gate)
```

A one-line summary (`{connections, handlers, prototypes, skipped, alreadyMigrated}`)
is printed to stderr.

The transform is **idempotent** — running it twice is a no-op — and
**deterministic**: new `Handler`/`PrototypeHandler`/binding ids are derived
(UUIDv5) from the source Thing ids, so the same input always yields the same
output and re-runs never create duplicates.

## Migrating an existing seed by hand

If you maintain your own seed, you do **not** need to hand-edit it — run
`migrate.js --write`. For reference, here is what it does to each service, using
`consumes` (a Metabolism predicate) as the example:

Before:

```jsonc
// consumes — selector AND launch info fused together
{ "Name": "consumes", "Properties": {
    "ExecutablePath": { "value": ".../Metabolism.dll" },
    "ServicePort":    { "value": 7102 },
    "ServiceArgs":    { "value": "--mode=consumes" },
    "onLoad":         { "value": true } } }
```

After:

```jsonc
{ "Name": "consumes", "Properties": {                 // now just a selector
    "__IsConnection": { "value": true },
    "trigger":        { "value": "graph" } } }

{ "Name": "consumes handler", "Properties": {          // per-instance overrides
    "__IsHandler":  { "value": true },
    "ServicePort":  { "value": 7102 },
    "ServiceArgs":  { "value": "--mode=consumes" },
    "AutoStart":    { "value": true } } }

{ "Name": "Metabolism Prototype", "Properties": {      // shared by consumes + produces
    "__IsPrototypeHandler": { "value": true },
    "ExecutablePath":       { "value": ".../Metabolism.dll" },
    "RunMode":              { "value": "daemon" } } }

// relationships added:
//   consumes        --hasHandler--> consumes handler   (predicate flagged __BindsHandler)
//   consumes handler --is-->        Metabolism Prototype
```

What changes, in words:

1. **Launch props move off the connection.** `ExecutablePath`/`ServicePort`/
   `ServiceArgs`/`onLoad` are removed from the predicate / `EndpointService`
   Thing.
2. **A `Handler` is created** carrying only the per-instance overrides
   (`ServicePort`, `ServiceArgs`, and `AutoStart` — the renamed `onLoad`).
3. **A `PrototypeHandler` is created (or reused)** per distinct `ExecutablePath`
   and holds the binary + `RunMode`. Connections sharing a binary share one
   prototype.
4. **Two relationships are added**: `Connection --hasHandler--> Handler` and
   `Handler --is--> PrototypeHandler`.
5. **The connection is flagged** `__IsConnection` and gains a `trigger`
   (`http` when it had a `Subdomain`, otherwise `graph`). `Subdomain` stays on
   the connection as its selector.

### Not migrated

- The **`is`** predicate and any handled predicate with no `ExecutablePath` —
  these run in-process, not as a daemon (`skipped` in the summary).
- Type template Things (`EndpointService`, `Handled Predicate`) — their stale
  launch props are stripped so inheritance can't re-supply them, but they are
  not turned into connections.
- Anything already flagged `__IsConnection` (`alreadyMigrated` in the summary).

## Limitations

- `ContractId` and `TokenScope` are **not** populated — the old seed format
  didn't carry them (token scope lived in broker code). Set them on the
  `PrototypeHandler` after migration; see Feature #5615 phases 3–4.
- Resolves inherited launch props (own + `InheritedProperties`); if your seed
  uses a deeper or unusual inheritance arrangement, review the output.
