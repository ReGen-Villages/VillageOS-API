# VillageOS API

Client tools, GUI, and microservices for the [VillageOS](https://dev.azure.com/ReGenVillages/VillageOS) temporal graph platform.

## Projects

### GUI

**vos.GUI** — React application for graph visualization and interaction.

- Sigma.js v3 + graphology for graph rendering
- Three.js + `@thatopen/fragments` for the IFC Model viewer (Feature #5248; loader, picking, plan/section toolbar, and shared `NodeDetailPanel` all shipped)
- SignalR real-time updates with flash effects
- Zustand state management
- Dashboard, Graph, Model, Temporal, Things, and Properties pages

### CLI

**vos.CLI** — Command-line interface for the VillageOS broker.

- Interactive REPL with command history and tab completion
- CRUD commands (create, get, list, delete, set, query, find)
- Temporal queries (snapshot, mutations, property-versions, plant)
- Service management (start-service, stop-service)
- Seed export (serialize command)

### Microservices

| Service | Type | Description |
|---------|------|-------------|
| vos.ManagedMicroservice.IntegrationRegistry | Production | Data source registration with schema discovery and saga compensation |
| vos.ManagedMicroservice.EndpointCaller | Production | HTTP endpoint calling with JSONata response transforms |
| vos.ManagedMicroservice.Echo | Example | Minimal managed microservice demonstrating lifecycle |
| vos.ManagedMicroservice.Python | Example | Python/Flask-based microservice with Docker support |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) (for the GUI)

## Getting Started

### .NET projects (CLI, microservices)

```bash
dotnet restore
dotnet build
dotnet test
```

### GUI

```bash
cd vos.GUI
npm ci
npm run dev      # Development server on :5173 (proxies /api + /vosHub to https://localhost:7243)
npm run build    # Production build into ./dist
npm test         # Run tests
```

The GUI connects to the VillageOS broker at `https://localhost:7243` by default.

#### Building directly into a broker's wwwroot

Set `VOS_BROKER_WWWROOT` to an absolute path to have `npm run build` emit the
bundle straight into a broker's static file directory, e.g.:

```bash
VOS_BROKER_WWWROOT=/absolute/path/to/VillageOS/vos.Broker/wwwroot npm run build
```

When the env var is unset, the build lands in `vos.GUI/dist/` as a normal
local artifact.

## Documentation

Full documentation is available in the [VillageOS API Wiki](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki).

Key pages:
- [GUI](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/37/GUI) — visualization stack, tech spec, user guide
- [CLI](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/39/CLI) — command reference
- [Services](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/40/Services) — microservice documentation
- [API Reference](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki/wikis/VillageOS-API-Wiki/45/API-Reference) — REST endpoints, SignalR, authentication

## License

Copyright (c) ReGen Villages BV.

This project is dual-licensed:

- **AGPL v3** — Free for open source use. Derivative works must be open source and attribute ReGen Villages BV. See [LICENSE](LICENSE).
- **Commercial License** — Available from ReGen Villages BV for proprietary use. See [LICENSE-COMMERCIAL](LICENSE-COMMERCIAL.md).
