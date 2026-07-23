# VillageOS API

> ⚠️ **Prerelease — work in progress. Not production ready.**
> This is pre-1.0 software under active development. APIs, data shapes, seed formats, and behaviour can
> change without notice or backward compatibility, and interfaces marked "Production" in the table below
> describe intended scope, not a stability or support guarantee. Expect rough edges, incomplete features,
> and breaking changes. Do not rely on it for production workloads.

This repository holds the **client-facing tools** for [VillageOS](https://dev.azure.com/ReGenVillages/VillageOS), a *temporal graph platform* — a database that stores everything as connected "Things" and remembers how they change over time.

Three kinds of tool live here:

- **Trellis** — a web GUI for exploring and monitoring a model in the browser.
- **Taproot** — a command-line interface for the same operations.
- **Microservices** — small networked services that extend the platform (ingesting data, running simulations, orchestrating pipelines).

Everything talks to **Mycelium**, the VillageOS server that stores the graph and exposes the REST API and real-time event streams. (Mycelium itself lives in a separate repository.)

## Projects

### Trellis (GUI)

**vos.Trellis** — React application for graph visualization and interaction.

- Sigma.js v3 + graphology for graph rendering
- Three.js + `@thatopen/fragments` for the IFC Model viewer (loader, picking, plan/section toolbar, and shared `NodeDetailPanel`)
- Server-Sent Events (SSE) real-time updates with flash effects
- Zustand state management
- Config-driven **Operations** dashboard — a model supplies a JSON spec and Trellis renders KPI, chart, funnel, table, and leaderboard widgets against it (the GUI stays domain-agnostic)
- Dashboard, Operations, Graph, Model, Pipelines, Temporal, Things, Properties, and Logs pages

### Taproot (CLI)

**vos.Taproot** — Command-line interface for the Mycelium.

- Interactive REPL with command history and tab completion
- CRUD commands (create, get, list, delete, set, query, find)
- Temporal queries (snapshot, at, history, mutations)
- Service management (start service, stop service)
- Seed export (serialize command)

### Microservices

| Service | Type | Description |
|---------|------|-------------|
| vos.ManagedMicroservice.Delta | Production | Registers data sources against a single-rooted endpoint-template graph (`is`-inheritance), with schema discovery and saga compensation |
| vos.ManagedMicroservice.Tributary | Production | HTTP endpoint calling with JSONata response transforms; config-driven token-exchange auth + offset pagination (e.g. ESRI/ArcGIS) |
| vos.ManagedMicroservice.Metabolism | Production | Consume/produce simulation — decrements/increments a target property's quantity at a configured rate; backs the `consumes`/`produces` Handled Predicates |
| vos.ManagedMicroservice.Phloem | Production | Pipeline/DAG orchestrator — runs a user-authored DAG of microservice nodes; spawned synchronously through Mycelium, dispatches each node via endpoint-forward (see [MICROSERVICES.md §16](docs/MICROSERVICES.md)) |
| vos.ManagedMicroservice.Xylem | Production | IFC ingestion — accepts an `.ifc` upload (`POST /ingest`, merge or new-model), runs the `vos.Tools.IfcIngest` tool, and applies the graph to Mycelium; frees clients from a local ingest toolchain. Large files: `?async=true` returns a job id (poll `GET /ingest/jobs/{id}`) and the upload is streamed with a configurable cap |
| vos.ManagedMicroservice.EnergyBalance | Production | Site energy-balance simulation — sums generation (e.g. solar: PV area × resource × efficiency) against demand; a Handled-Predicate service in the same family as Metabolism |
| vos.ManagedMicroservice.WaterReserve | Production | Water-reserve simulation — tracks stored water against consumption (e.g. an emergency reserve under a supply failure) |
| vos.ManagedMicroservice.ModelBridge | Production | Generic bridge between a pipeline DAG and the model — reads a property off a Thing or writes a computed result back (see [MODELBRIDGE.md](docs/MODELBRIDGE.md)) |
| vos.ManagedMicroservice.CSharp.Echo | Example (C#) | Minimal managed microservice demonstrating the lifecycle — the canonical reference; also the reference pipeline DAG node |
| vos.ManagedMicroservice.Go.Echo | Example (Go) | The same handler in Go (standard library, zero deps) |
| vos.ManagedMicroservice.Node.Echo | Example (Node/TS) | The same handler in TypeScript (Node built-ins, zero runtime deps) |
| vos.ManagedMicroservice.Python.Echo | Example (Python) | The same handler in FastAPI |
| vos.ManagedMicroservice.Rust.Echo | Example (Rust) | The same handler in Axum |

Writing your own handler in any language? See **[docs/MICROSERVICE_AUTHORING.md](docs/MICROSERVICE_AUTHORING.md)** — the language-agnostic contract (HTTP + one HS256 JWT) that every example above implements.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20.19+](https://nodejs.org/) (for Trellis; required by Vite 7)

## Getting Started

### .NET projects (Taproot, microservices)

```bash
dotnet restore
dotnet build
dotnet test
```

### Trellis (GUI)

```bash
cd vos.Trellis
npm ci
npm run dev      # Development server on :5173 (proxies /api to https://localhost:7243)
npm run build    # Production build into ./dist
npm test         # Run tests
```

Trellis connects to the Mycelium at `https://localhost:7243` by default.

#### Building directly into a Mycelium's wwwroot

Set `VOS_MYCELIUM_WWWROOT` to an absolute path to have `npm run build` emit the
bundle straight into a Mycelium's static file directory, e.g.:

```bash
VOS_MYCELIUM_WWWROOT=/absolute/path/to/VillageOS/vos.Mycelium/wwwroot npm run build
```

When the env var is unset, the build lands in `vos.Trellis/dist/` as a normal
local artifact.

## Documentation

In-repo docs live in **[docs/](docs/README.md)** — an indexed map grouped by client tools, platform concepts, and microservice authoring.

The full published documentation is also available in the [VillageOS API Wiki](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki).

Key pages:

- [Trellis](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FTrellis) — visualization stack, tech spec, user guide
- [Taproot](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FTaproot) — command reference
- [Services](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FServices) — microservice documentation
- [API Reference](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FAPI-Reference) — REST endpoints, SSE streams, authentication

## License

Copyright (c) ReGen Villages BV.

This project is dual-licensed:

- **AGPL v3** — Free for open source use. Derivative works must be open source and attribute ReGen Villages BV. See [LICENSE](LICENSE).
- **Commercial License** — Available from ReGen Villages BV for proprietary use. See [LICENSE-COMMERCIAL](LICENSE-COMMERCIAL.md).
