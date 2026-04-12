# Building VillageOS Microservices

The full microservice development guide lives in the VillageOS broker repo:

- **[Microservice Guide](https://dev.azure.com/ReGenVillages/VillageOS/_wiki/wikis/VillageOS-Wiki/11/Microservices)** — lifecycle, daemon management, HTTP handler patterns
- **[Relationship Services](https://dev.azure.com/ReGenVillages/VillageOS/_wiki/wikis/VillageOS-Wiki/12/Broker)** — predicate handlers, fan-out, SignalR events

## Quick Start

1. Create an ASP.NET Web API project
2. Add NuGet references to `vos.Auth.Shared` and `vos.Microservice.Shared`
3. Implement a `/handle` endpoint that receives `{ SubjectId, PredicateId, TargetId, ... }`
4. Register with the broker at startup via `POST /api/broker/register`
5. See the example services in this repo: `vos.ManagedMicroservice.Echo`, `vos.ManagedMicroservice.EndpointCaller`, `vos.ManagedMicroservice.Python`
