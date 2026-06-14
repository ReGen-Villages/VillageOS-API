# VillageOS API

Client tools, the Trellis GUI, and microservices for the [VillageOS](https://dev.azure.com/ReGenVillages/VillageOS) temporal graph platform.

## Projects

### Trellis (GUI)

**vos.Trellis** — React application for graph visualization and interaction.

- Sigma.js v3 + graphology for graph rendering
- Three.js + `@thatopen/fragments` for the IFC Model viewer (Feature #5248; loader, picking, plan/section toolbar, and shared `NodeDetailPanel` all shipped)
- SignalR real-time updates with flash effects
- Zustand state management
- Dashboard, Graph, Model, Temporal, Things, and Properties pages

### Taproot (CLI)

**vos.Taproot** — Command-line interface for the VillageOS broker.

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
| vos.ManagedMicroservice.Echo | Example | Minimal managed microservice demonstrating lifecycle |
| vos.ManagedMicroservice.Python | Example | Python/Flask-based microservice with Docker support |

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
npm run dev      # Development server on :5173 (proxies /api + /vosHub to https://localhost:7243)
npm run build    # Production build into ./dist
npm test         # Run tests
```

Trellis connects to the VillageOS broker at `https://localhost:7243` by default.

#### Building directly into a broker's wwwroot

Set `VOS_BROKER_WWWROOT` to an absolute path to have `npm run build` emit the
bundle straight into a broker's static file directory, e.g.:

```bash
VOS_BROKER_WWWROOT=/absolute/path/to/VillageOS/vos.Broker/wwwroot npm run build
```

When the env var is unset, the build lands in `vos.Trellis/dist/` as a normal
local artifact.

## Documentation

Full documentation is available in the [VillageOS API Wiki](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki).

Key pages:
- [Trellis](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FTrellis) — visualization stack, tech spec, user guide
- [Taproot](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki?pagePath=%2FTaproot) — command reference
- [Services](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/40/Services) — microservice documentation
- [API Reference](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/45/API-Reference) — REST endpoints, SignalR, authentication

## License

Copyright (c) ReGen Villages BV.

This project is dual-licensed:

- **AGPL v3** — Free for open source use. Derivative works must be open source and attribute ReGen Villages BV. See [LICENSE](LICENSE).
- **Commercial License** — Available from ReGen Villages BV for proprietary use. See [LICENSE-COMMERCIAL](LICENSE-COMMERCIAL.md).
