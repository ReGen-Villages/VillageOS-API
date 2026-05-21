# Contract Validation — Foundation Layer

> **Status:** Phase 1 landed (Feature #5419). The schemas + registry + validator
> live inside `vos.ManagedMicroservice.Shared` as a **dormant** library — no production
> code path consumes them yet. Subsequent phases wire the validator into the
> request pipeline; see *Roadmap* below.

## 1. What this is

A small set of JSON Schema artifacts plus a runtime that loads and validates
against them. The schemas pin the wire format of the broker ↔ microservice
payloads that are stable today, so future changes to those payloads become a
schema diff in code review rather than a silent runtime surprise.

Schemas live under `vos.ManagedMicroservice.Shared/Contracts/Schemas/` and are
embedded as resources in the shared assembly. The validator runtime lives in
`vos.ManagedMicroservice.Shared/Contracts/Validation/`.

## 2. Why now

Every broker ↔ microservice payload is hand-typed in C# on this side and
hand-typed again in the broker repo. There is no machine-readable contract;
drift is invisible until a runtime error or silent field loss. The fix isn't
"add error handling" — it's making the wire format itself the contract.

This phase introduces only the artifacts and the validator. Adoption is a
deliberate later phase so the foundation can be reviewed independently of any
behaviour change.

## 3. Schemas in scope (Phase 1)

| Schema | Producer → Consumer | Source of truth in code |
|---|---|---|
| `broker-register-request` | every microservice → broker `POST /api/broker/register` | `BrokerClientBase.RegisterAsync` |
| `token-response` | broker `POST /api/auth/token` → every microservice | `BrokerClientBase.GetTokenAsync` |
| `handle-request-metabolism` | broker → Metabolism `POST /handle` | `vos.ManagedMicroservice.Metabolism.Models.HandleRequest` |
| `relationship-property-changed-event` | broker `/vosHub` → Metabolism (SignalR) | `vos.ManagedMicroservice.Metabolism.Services.BrokerClient.OnRelationshipPropertyChanged` |

Each schema uses `additionalProperties: false` on every object subschema (strict
by default per the project's pre-release / no-shims convention).

Schemas deliberately **excluded** from Phase 1:

- `ack-response` — the envelope from `docs/DELIVERY.md §3.3` is a design
  proposal, not on the wire. Lands with the Feature that implements DELIVERY.md.
- `health-response` — the envelope from `docs/DELIVERY.md §8` is similarly
  aspirational. Same disposition.
- Every other `handle-request-*` — only Metabolism has a typed body today; Echo
  and Delta/Tributary read raw or service-specific shapes. Add a schema per
  service when its body shape stabilises.

## 4. Public API

### `SchemaRegistry`

Eagerly loads every embedded schema and exposes them by `$id`:

```csharp
var registry = new SchemaRegistry();
JsonSchema schema = registry.Get("https://villageos/contracts/broker-register-request.schema.json");
JsonSchema sameSchema = registry.Get<BrokerRegisterRequest>();  // via [ContractSchema]
```

One instance per process is sufficient. The constructor throws
`InvalidOperationException` at startup if any embedded schema is missing
`$id` or two schemas share the same `$id` — design-time mistakes fail closed.

### `[ContractSchema(id)]`

Decorates a DTO so `SchemaRegistry.Get<T>()` can resolve it by type:

```csharp
[ContractSchema("https://villageos/contracts/broker-register-request.schema.json")]
public sealed record BrokerRegisterRequest(/* ... */);
```

### `SchemaValidator`

Stateless validator returning a structured result:

```csharp
var validator = new SchemaValidator();
ContractValidationResult result = validator.Validate(json, schema);

if (!result.IsValid)
    foreach (var e in result.Errors)
        log.LogWarning("contract violation {Path} [{Code}]: {Message}", e.Path, e.Code, e.Message);

// Strict mode:
validator.ValidateOrThrow(json, schema, schemaId);   // throws ContractValidationException on failure
```

`ContractValidationError.Code` is a stable public code family — decoupled from
NJsonSchema's internal `ValidationErrorKind` enum:

| Code | Meaning |
|---|---|
| `Required` | A `required` property is missing. |
| `AdditionalProperties` | An unknown property is present (strict mode). |
| `Type` | The JSON type does not match `type` (string/integer/number/array/...). |
| `Format` | The value violates `format` (`uuid`, `uri`, `date-time`, …). |
| `ArrayLength` | `minItems` / `maxItems` violated. |
| (other) | Raw NJsonSchema `ValidationErrorKind` name as a fall-through. |

## 5. Adding a schema

1. Drop a `.schema.json` file under `vos.ManagedMicroservice.Shared/Contracts/Schemas/`
   with a unique `$id` of the form
   `https://villageos/contracts/<name>.schema.json`.
2. Set `additionalProperties: false` on every object subschema.
3. Add fixtures under
   `Tests/vos.ManagedMicroservice.Shared.Contracts.Tests/Fixtures/<schema-folder>/`:
   - `valid.json` — at least one positive case.
   - `invalid-<reason>.json` — one or more negative cases.
4. Add the schema id to the positive/negative tables in
   `SchemaValidatorTests`. `SchemaSelfValidityTests` discovers it automatically.

Schemas authored against Draft 2020-12 by default. The
`relationship-property-changed-event` schema uses Draft 7 because NJsonSchema's
runtime validator does not implement Draft 2020-12 `prefixItems`; switch back
to Draft 2020-12 if NJsonSchema gains support.

## 6. Tests + coverage

`Tests/vos.ManagedMicroservice.Shared.Contracts.Tests/` runs alongside the rest of
the solution under `dotnet test`. The new assembly is excluded from coverage
measurement via the existing `ModulePath` filter in `coverage.runsettings`;
the production code lands under `vos.ManagedMicroservice.Shared`'s existing
threshold (100% line, 100% branch — unchanged by Phase 1).

The `LoadEmbeddedRawSchemas` host-side enumeration is marked
`[ExcludeFromCodeCoverage]` because its branches (resource-name filter,
defensive null-stream throw) are not reachable through the test surface; the
parsing and registration logic it feeds *is* covered, via the internal
`SchemaRegistry` constructor that takes raw `(name, json)` pairs.

## 7. Roadmap (later phases)

Each phase is its own Feature work item with child Tasks. No code is shared
across phases beyond what already exists; each phase adopts the foundation in
one well-defined direction.

| Phase | Wires the validator into… | Notes |
|---|---|---|
| 2 | Inbound `/handle` middleware (`app.UseRequestContractValidation()`) | Schema-violation → `400` matching the Ack envelope from `docs/DELIVERY.md §3.3`. |
| 3 | Outbound `BrokerClientBase` calls | Debug = throw, Release = log+metric, so a missing schema never blocks production. |
| 4 | SignalR receive-side in Metabolism | Validates `RelationshipPropertyChanged` events; malformed events surface as a typed event rather than throwing into the SignalR pipeline. |
| 5 | GUI runtime validation | TS types generated from the same schemas; opt-in dev-only validation. |
| 6 | CI drift gate | `Tools/Test-ContractDrift.ps1` fails the build if any DTO drifts from its schema. |

## 8. Cross-references

- `docs/DELIVERY.md` — the Ack envelope and inbound delivery contract Phase 2
  will dovetail with. Phase 1 deliberately does not pre-empt that work.
- `docs/MICROSERVICE-TEMPLATE.md` — every microservice will adopt the validator
  via the template once Phase 2 lands.
- `docs/TEST-STATE.md` — the TDD discipline applies to schemas too: failing
  test (negative fixture) first, then the schema rule that satisfies it.
- `CLAUDE.md` — *Pre-release, no compatibility shims* — why
  `additionalProperties: false` is the default.
