# Contracts

JSON Schema (Draft 2020-12) definitions for Mycelium &harr; microservice payloads, plus the runtime that loads and validates against them.

This directory is the source of truth for the wire format of every contract that is stable across services. Each schema has a `$id` URI of the form `https://villageos/contracts/<name>.schema.json` and is embedded as a resource in `vos.Service.Shared.dll` (see `vos.Service.Shared.csproj`).

## Layout

```text
Contracts/
  Schemas/           <- JSON Schema source files (embedded resources)
  Validation/        <- SchemaRegistry, SchemaValidator, ContractValidationResult
```

## Adding a schema

1. Drop the `.schema.json` file under `Schemas/` with a unique `$id`.
2. Set `additionalProperties: false` on every object subschema (strict by default per project convention).
3. Add positive and negative fixtures under `Tests/vos.Service.Shared.Contracts.Tests/Fixtures/`.
4. Run `dotnet test` &mdash; `SchemaSelfValidityTests` finds the new schema by file discovery and exercises it.

## Where the validator runs

- **Inbound `/handle` bodies**, through the `UseRequestContractValidation` middleware, on a route that opts in with `RequireContract<T>()`. Metabolism's `/handle` does.
- **Outbound calls in `MyceliumClientBase`**: the registration body, the token response, and the body of each write helper.
- **Metabolism's own outbound calls**: the quantity adjustment and the relationship increment.

A violation throws in a Debug build and logs a warning in a Release build. See `docs/SERVICES.md` §9 for the whole of it.
