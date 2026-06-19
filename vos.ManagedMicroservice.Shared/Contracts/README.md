# Contracts

JSON Schema (Draft 2020-12) definitions for Mycelium &harr; microservice payloads, plus the runtime that loads and validates against them.

This directory is the source of truth for the wire format of every contract that is stable across services. Each schema has a `$id` URI of the form `https://villageos/contracts/<name>.schema.json` and is embedded as a resource in `vos.ManagedMicroservice.Shared.dll` (see `vos.ManagedMicroservice.Shared.csproj`).

## Layout

```text
Contracts/
  Schemas/           <- JSON Schema source files (embedded resources)
  Validation/        <- SchemaRegistry, SchemaValidator, ContractValidationResult
```

## Adding a schema

1. Drop the `.schema.json` file under `Schemas/` with a unique `$id`.
2. Set `additionalProperties: false` on every object subschema (strict by default per project convention).
3. Add positive and negative fixtures under `Tests/vos.ManagedMicroservice.Shared.Contracts.Tests/Fixtures/`.
4. Run `dotnet test` &mdash; `SchemaSelfValidityTests` will exercise the new schema automatically (file discovery).

## Adoption posture

Phase 1 ships the schemas + validator as a *dormant library* &mdash; no production code path consumes them yet. Subsequent phases wire the validator into:

- Phase 2: inbound `/handle` middleware (`UseRequestContractValidation`).
- Phase 3: outbound checks in `MyceliumClientBase`.
- Phase 4: SSE change-stream receive-side in Metabolism (via the shared `SubscriptionClient`).
- Phase 5: GUI runtime validation (TS types generated from the same schemas).

See `docs/MICROSERVICES.md` §9 for the full design.
