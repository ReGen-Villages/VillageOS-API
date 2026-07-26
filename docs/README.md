# VillageOS API — Documentation

Guides and references for the client-facing side of VillageOS. New here? Start with the
[repository README](../README.md) for what this project is and how to build it, then use the
map below.

## Client tools

| Doc | For | What it covers |
|-----|-----|----------------|
| [TRELLIS.md](TRELLIS.md) | GUI users **and** GUI developers | The web GUI. Part 1 is a click-around user guide; Part 2 is the technical spec (architecture, pages, state, API/SSE layer). |
| [TAPROOT_USER_GUIDE.md](TAPROOT_USER_GUIDE.md) | CLI users | The command-line interface — commands, examples, and workflows. |
| [PIPELINE_PLAYGROUND.md](PIPELINE_PLAYGROUND.md) | Anyone exploring the Pipelines page | How to load and use the ready-made example DAGs and services that exercise the Trellis Pipeline editor. |

## Platform concepts

Approachable for a general reader as well as an engineer.

| Doc | What it covers |
|-----|----------------|
| [RELATIONSHIP_SERVICES.md](RELATIONSHIP_SERVICES.md) | How a relationship in the graph can trigger a microservice ("handled predicates"), and the built-in `is` inheritance. |
| [METABOLISM.md](METABOLISM.md) | The consume/produce simulation service behind the `consumes` / `produces` predicates. |
| [TRIBUTARY.md](TRIBUTARY.md) | Pulling data in from external HTTP APIs (with JSONata response transforms) as configuration, not code. |
| [DELTA.md](DELTA.md) | The endpoint-registration service that provisions and validates data-source endpoints. |
| [MODELBRIDGE.md](MODELBRIDGE.md) | The generic bridge between a pipeline DAG and the model (read a property, or write a computed result back). |
| [LAND_INTAKE.md](LAND_INTAKE.md) | **Design, not yet built.** Taking in a piece of land and analysing it — the intake wizard, open-data discovery, and the site-analysis pipeline. Written for a general reader; render to a print-ready PDF with [tools/docs-pdf](../tools/docs-pdf/). |

## Authoring microservices

Reference material for engineers building or hosting services. Denser by design.

| Doc | What it covers |
|-----|----------------|
| [MICROSERVICE_AUTHORING.md](MICROSERVICE_AUTHORING.md) | The language-agnostic contract for writing your own handler (HTTP + one HS256 JWT), with reference implementations in five languages. |
| [MICROSERVICE_CONTRACT.md](MICROSERVICE_CONTRACT.md) | The precise wire contract — endpoints, subscribe/SSE, and write-back kinds. |
| [MICROSERVICES.md](MICROSERVICES.md) | The canonical reference for the C# managed-microservice host and the services built on it. |
| [MICROSERVICE_HOST_ROADMAP.md](MICROSERVICE_HOST_ROADMAP.md) | Roadmap for consolidating the shared host across services. |

> The full published documentation also lives in the
> [VillageOS API Wiki](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki).
