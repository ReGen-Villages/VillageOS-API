# VillageOS API

> ⚠️ **Prerelease — work in progress. Not production ready.**
> This is pre-1.0 software under active development. APIs, data shapes, seed formats, and behaviour can
> change without notice or backward compatibility, and interfaces marked "Production" in the table below
> describe intended scope, not a stability or support guarantee. Expect rough edges, incomplete features,
> and breaking changes. Do not rely on it for production workloads.

This repository holds the **client-facing tools** for VillageOS, a *temporal graph platform* — a database that stores everything as connected "Things" and remembers how they change over time.

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
- Live updates from the Mycelium's event stream, with each changed node flashing on the graph
- Zustand state management
- An **Operations** dashboard drawn from a JSON page description the model holds: figures, charts, funnels, tables, leaderboards and the widgets that write, each binding read through Tendril, so the console holds no word of any one domain
- Dashboard, Operations, Compose, Design, Land intake, Graph, Model, Pipelines, Temporal, Things, Properties, Logs and Requests pages, plus every page the model publishes (the land-intake template's Submissions review among them) and every page the platform declares for the signed-in account (Accounts and API keys, for an administrator), drawn like a model's own

### Tendril (page reader and writing widgets)

**vos.Tendril** — the package `@tendril-ui/tendril`: the binding reader, the page declarations and the
widgets that write, so an application other than Trellis draws a model's pages the same way. Trellis
uses it from the folder beside it. See [vos.Tendril/README.md](vos.Tendril/README.md).

### Pages (page reader for C#)

**vos.Pages** — a class library that lists and reads a model's pages with Tendril's own reader, run
inside a C# process, so a program that is not a browser answers a page as Trellis draws it. See
[vos.Pages/README.md](vos.Pages/README.md).

### Taproot (CLI)

**vos.Taproot** — Command-line interface for the Mycelium.

- Interactive shell with command history and line editing
- Commands to create, read, change and delete Things (create, get, list, delete, set, rename, retype, query, find), with narrowed lists
- Ranges and states, including each Thing's state history
- Temporal queries (snapshot, at, history, mutations)
- Service management (list, start and stop a service; call an endpoint or a handler), pipelines, the event stream and the logs
- Seeds, models and fragments (serialize, deserialize, plant, apply, ingest), submissions and accounts

**vos.Taproot.Client** — the library Taproot reaches the Mycelium through: the client for its routes,
name lookup, and reading a model by its role bindings. A program other than Taproot references it to
make the same calls.

### Microservices

| Service | Type | Description |
|---------|------|-------------|
| vos.Service.Delta | Production | Registers data sources under endpoint templates that inherit from one root through `is`, discovers each source's schema, and undoes the steps already taken when a later one fails |
| vos.Service.Tributary | Production | Calls an HTTP endpoint and reshapes the answer with a JSONata expression, signing in by exchanging a token and paging by offset where the endpoint's settings ask (as an ArcGIS server does); also a pipeline step that fetches, or asks for a Thing's history to be judged |
| vos.Service.Metabolism | Production | Consume and produce simulation: lowers or raises a target property's quantity at a configured rate, for the `consumes` and `produces` predicates |
| vos.Service.Phloem | Production | Pipeline orchestrator: runs a pipeline of service nodes drawn in the model, started by a request through Mycelium, a relationship, a Thing entering a state or the clock, and calls each node through Mycelium's endpoint forwarding (see [SERVICES.md §16](docs/SERVICES.md)) |
| vos.Service.Xylem | Production | IFC ingestion — accepts an `.ifc` upload (`POST /ingest`, merge or new-model), runs the `vos.Tools.ModelIngest` tool, and applies the graph to Mycelium; frees clients from a local ingest toolchain. Large files: `?async=true` returns a job id (poll `GET /ingest/jobs/{id}`) and the upload is streamed with a configurable cap |
| vos.Service.Forage | Production | Discovery's pipeline steps: resolves a site against every data source covering it, plans each source's calls for the discovery drawing, records what they came to and marks the site discovered (see [FORAGE.md](docs/FORAGE.md)) |
| vos.Service.Intake | Production | Takes a land-intake submission and composes the Site, Parcel and study it becomes, applied as one all-or-nothing fragment; registers with nothing and holds its own credential (see [LAND_INTAKE.md](docs/LAND_INTAKE.md)) |
| vos.Service.Feedback | Production | Takes a bug report or an idea from a signed-in person's report panel and files it as an Azure DevOps work item, with the screenshot attached; asks the platform to accept the caller's token, holds the DevOps access token, and reads each application's project and area from the `Destinations` section of its settings |
| vos.Service.EnergyBalance | Production | Site energy verdict as a pipeline node: from the generation inputs wired into it (solar array area, resource and efficiency, and other generation) and the consumption, answers solar and total generation, the share of consumption and whether the site is net positive |
| vos.Service.WaterReserve | Production | Water-reserve simulation — tracks stored water against consumption (e.g. an emergency reserve under a supply failure). A pipeline node only: a site study declares all four of its figures as formulas, so nothing calls it about a study |
| vos.Service.Transect | Production | Measures a parcel's border again as a pipeline node, with the area method the model configures and the formula the intake measures a submission with, and writes the area and the method onto the parcel. Launched by Mycelium when a pipeline first calls it. |
| vos.Service.Stomata | Production | Works out a study's climate as a pipeline node: from its site's hourly history it writes each hour's wind rose cell, humidity ratio, and whether the windows can be open on the hour's air and on the day's, then the time the study was worked out. Launched by Mycelium when a pipeline first calls it. |
| vos.Service.Phyllotaxis | In development | The layout generator, which draws the village on a site. So far it holds the placement on the terrain: the reservoirs on the lowest gentle ground that water drains to, the village core on flat ground above them, the wind turbines on the highest ground spaced by rotor, the solar fields over the crops where the land throws least shade, and the waste plant downwind beyond its buffer. Not yet a pipeline node |
| vos.Service.ModelBridge | Production | Generic bridge between a pipeline and the model — reads a property off a Thing or writes a computed result back (see [MODELBRIDGE.md](docs/MODELBRIDGE.md)) |
| vos.Service.Quadrat | In development (Python) | Raster reader — the value at a point, statistics over a polygon or box, the area of each land-cover class or elevation band in it, or the values along a line, from a GeoTIFF; a command line, and a service answering pipeline nodes (see [its README](vos.Service.Quadrat/README.md)) |
| vos.Service.CSharp.Echo | Example (C#) | The smallest service the platform manages, showing its whole life, from launch to shutdown; the reference service, and the reference pipeline node |
| vos.Service.Go.Echo | Example (Go) | The same handler in Go (standard library, zero deps) |
| vos.Service.Node.Echo | Example (Node/TS) | The same handler in TypeScript (Node built-ins, zero runtime deps) |
| vos.Service.Python.Echo | Example (Python) | The same handler in FastAPI |
| vos.Service.Rust.Echo | Example (Rust) | The same handler in Axum |

Writing your own handler in any language? See **[docs/SERVICE_AUTHORING.md](docs/SERVICE_AUTHORING.md)** — the contract any language can meet (HTTP and one JSON Web Token signed on the P-256 elliptic curve) that every example above implements.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22](https://nodejs.org/) (for Trellis; Vite 7 accepts 20.19 or later, and the build uses 22)

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
npm run build    # Production build into ./dist (type-checks first)
npm test         # Run tests
npm run test:integration   # Tests that need a running Mycelium (see below)
npm run lint     # Lint
npm run rewrite:saved-drawing   # Rewrites the saved-drawing fixture (see below)
```

The build runs `npm run lint`, `npm test` and `npm run build`, so a failure in any of them fails the
build — and on `develop` stops the wiki publish. It runs when `develop` or
`main` moves and for every pull request into `develop` or `main` on GitHub. A pull request from a
fork is never built: an owner who has read it pushes its commits to a branch in this repository and
opens the pull request from that branch, which is built. Run them before pushing rather than finding
out from the build.

`npm run rewrite:saved-drawing` rewrites
`Tests/vos.BrokerContract.Tests/AgainstTheEngine/Fixtures/the-pipelines-page-saves-a-wait-and-a-relate-box.json`
from what the Pipelines page saves now. Run it only after a deliberate change to what the page sends
on save, and read the fixture's diff. `savedDrawing.test.ts` fails until the fixture matches, and the
engine case `PhloemRunsADrawingThePipelinesPageSavedTests` replays the fixture as an editor.

A build **against `main`** compiles Release — a merge to it, and a pull request targeting it, since
a merge is too late to learn that Release does not compile. Every other build compiles Debug, which
is what you run locally. The difference is not only optimization here: `MyceliumClientBase` throws
on an outbound contract violation in Debug and logs it in Release. Every build generates and converts
the documentation's wiki pages and proves the publishing token; `develop` alone publishes them to the
GitHub wiki. No build step touches an Azure DevOps wiki. The repository itself lives on GitHub;
nothing copies it.

`npm test` runs offline. `npm run test:integration` covers what only a live
platform can answer: that the property type names Trellis holds are
the ones the platform's write routes accept, so a type added on one side and not
the other is caught rather than surfacing later as a property the GUI
mishandles. It needs a Mycelium running, so it is not part of the build above;
the VillageOS pipeline runs it in every build it does, because that build can
start one. Point it elsewhere with `VOS_INTEGRATION_URL`,
`VOS_INTEGRATION_USERNAME` and `VOS_INTEGRATION_PASSWORD`.

Trellis connects to the Mycelium at `https://localhost:7243` by default.

#### Building directly into a Mycelium's wwwroot

Set `VOS_MYCELIUM_WWWROOT` to an absolute path to have `npm run build` write the
bundle straight into a Mycelium's static file folder:

```bash
VOS_MYCELIUM_WWWROOT=/absolute/path/to/VillageOS/vos.Mycelium/wwwroot npm run build
```

When the variable is unset, the build lands in `vos.Trellis/dist/`.

### Testing a service against the real platform

`Tests/vos.BrokerContract.Tests` runs a service's own broker client against a **real Mycelium started
inside the test process** — real routes, real inheritance, real write semantics, over no network.
Every other service suite answers the platform with a stand-in it wrote itself, which encodes what
its author believed the platform does: a service writing a shape the platform refuses passes every
case written against a stand-in that accepts it.

It needs the platform's engine, which lives in the VillageOS repository, so it does not run from a
plain `dotnet test` here. Stage one and run it:

```bash
./ci/stage-the-engine.sh                 # from a VillageOS checkout beside this one
./ci/stage-the-engine.sh /path/to/VillageOS   # or name it
dotnet test Tests/vos.BrokerContract.Tests
```

With nothing staged every case still compiles and each is reported as skipped, saying no engine is
staged, rather than a green run over nothing. The VillageOS pipeline runs it in every build it does, because
that build has the engine to stage; point it at a staged engine elsewhere with `-p:VosRelease=`.

## Documentation

In-repo docs live in **[docs/](docs/README.md)** — an indexed map grouped by client tools, platform concepts, and microservice authoring.

The same documentation is published to the [VillageOS API Wiki](https://github.com/ReGen-Villages/VillageOS-API/wiki), which is **generated from the files in `docs/`** on every build of `develop` — edit the file, never the wiki page. See [tools/docs-to-wiki](tools/docs-to-wiki/).

Key pages:

- [Field Guide](https://github.com/ReGen-Villages/VillageOS-API/wiki/Field%20Guide) — the whole platform for every reader: the console page by page, the command line command by command, and the page-authoring contract
- [Services](https://github.com/ReGen-Villages/VillageOS-API/wiki/Services) — the service host, the wire contract, and each service

## License

Copyright (c) ReGen Villages BV.

This project is dual-licensed:

- **AGPL v3** — Free for open source use. Derivative works must be open source and attribute ReGen Villages BV. See [LICENSE](LICENSE).
- **Commercial License** — Available from ReGen Villages BV for proprietary use. See [LICENSE-COMMERCIAL](LICENSE-COMMERCIAL.md).
