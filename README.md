# VillageOS API

Client tools, GUI, and microservices for the [VillageOS](https://dev.azure.com/ReGenVillages/VillageOS) temporal graph platform.

## Projects

### GUI

**vos.GUI** — React application for graph visualization and interaction.

- Sigma.js v3 + graphology for graph rendering
- MapLibre GL for geo-positioned map views with building footprints
- Three.js for per-building 3D detail (code-split, lazy-loaded)
- SignalR real-time updates with flash effects
- Zustand state management
- Dashboard, graph, map, temporal, and search pages

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
npm run dev      # Development server
npm run build    # Production build
npm test         # Run tests
```

The GUI connects to the VillageOS broker at `https://localhost:5001` by default.

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
