# GUI Map and 3D Rendering

The full GUI technical spec lives in the VillageOS broker repo:

- **[GUI Technical Spec](https://dev.azure.com/ReGenVillages/VillageOS/_wiki/wikis/VillageOS-Wiki/37/GUI)** — Sigma.js graph, MapLibre map, Three.js 3D, toolbar controls
- **[GUI User Guide](https://dev.azure.com/ReGenVillages/VillageOS/_wiki/wikis/VillageOS-Wiki/38/GUI)** — walkthrough of all pages and features

## Key Architecture

- **Graph rendering**: Sigma.js v3 + graphology (WebGL)
- **Map layer**: MapLibre GL + `@sigma/layer-maplibre` + Carto dark-matter tiles
- **3D buildings**: Three.js via `@react-three/fiber` (code-split, lazy-loaded)
- **Map zoom/fit**: routed directly through MapLibre in map mode (not Sigma) via `lib/mapInstance.ts`
- **Degenerate bbox floor**: `lib/bboxFloor.ts` prevents collapse to zoom 22 on 1-2 coincident nodes
- **Orphan site filter**: `hideOrphanSites` toggle reads `__IsMapSurfaceThing` flag from the IFC importer
- **Containment identification**: reads `__IsMapContainmentPredicate` flag — no hardcoded predicate names
