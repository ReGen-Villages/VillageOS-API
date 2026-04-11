import type { Map as MaplibreMap } from 'maplibre-gl';

/**
 * Module-level ref to the currently-bound MapLibre map instance. Written by
 * MaplibreLayer on bind/clean; read by GraphToolbar so its zoom/fit buttons
 * can act on the map directly in map mode instead of routing through Sigma's
 * camera (which gets clamped back by @sigma/layer-maplibre's sync loop).
 */
let mapInstance: MaplibreMap | null = null;

export function setMapInstance(m: MaplibreMap | null): void {
  mapInstance = m;
}

export function getMapInstance(): MaplibreMap | null {
  return mapInstance;
}
