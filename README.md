# VillageOS API

Client tools, the Trellis GUI, and microservices for the [VillageOS](https://dev.azure.com/ReGenVillages/VillageOS) temporal graph platform.

## Projects

### Trellis (GUI)

**vos.Trellis** — React application for graph visualization and interaction.

- Sigma.js v3 + graphology for graph rendering
- Three.js + `@thatopen/fragments` for the IFC Model viewer (loader, picking, plan/section toolbar, and shared `NodeDetailPanel`)
- Server-Sent Events (SSE) real-time updates with flash effects
- Zustand state management
- Dashboard, Graph, Model, Temporal, Things, and Properties pages

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
| vos.ManagedMicroservice.Echo | Example (C#) | Minimal managed microservice demonstrating the lifecycle — the canonical reference; also the reference pipeline DAG node |
| vos.ManagedMicroservice.Go | Example (Go) | The same handler in Go (standard library, zero deps) |
| vos.ManagedMicroservice.Node | Example (Node/TS) | The same handler in TypeScript (Node built-ins, zero runtime deps) |
| vos.ManagedMicroservice.Python | Example (Python) | The same handler in FastAPI |
| vos.ManagedMicroservice.Rust | Example (Rust) | The same handler in Axum |

Writing your own handler in any language? See **[docs/MICROSERVICE_AUTHORING.md](docs/MICROSERVICE_AUTHORING.md)** — the language-agnostic contract (HTTP + one HS256 JWT) that every example above implements.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/) (for Trellis)

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

Full documentation is available in the [VillageOS API Wiki](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki).

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
