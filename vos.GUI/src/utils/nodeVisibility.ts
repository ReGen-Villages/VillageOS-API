import type Graph from 'graphology';
import type { SearchOptions } from './searchFilter';
import { buildStringMatcher } from './searchFilter';

// ── Shared orbit constants ───────────────────────────────────────────────
// Radial offsets in degrees for different orbital contexts.

/** Orbit radius for non-geo neighbors revealed by selection (~17 m). */
export const ORBIT_RADIUS_SELECTION = 0.00015;

/** Orbit radius for containment children (~17 m, same as selection). */
export const ORBIT_RADIUS_CONTAINMENT = 0.00015;

/** Orbit radius for fan-out of overlapping nodes (~165 m). */
export const ORBIT_RADIUS_FANOUT = 0.0015;

/** Check whether a thing is a containment predicate (stamped by the IFC importer). */
export function isContainmentPredicate(props: Record<string, unknown> | undefined): boolean {
  return props?.__IsMapContainmentPredicate === true;
}

/** Check whether a raw VosThing has latitude/longitude properties. */
export function hasGeoProperties(props: Record<string, unknown>): boolean {
  return typeof props?.latitude === 'number' && typeof props?.longitude === 'number';
}

/** Check whether a node has geographic coordinates (geometry + lat/lng). */
export function isGeoNode(attrs: Record<string, unknown>): boolean {
  return !!attrs.hasGeometry && typeof attrs.lat === 'number' && typeof attrs.lng === 'number';
}

/**
 * Compute the position of a single node in a radial orbit around a center point.
 * Used to fan out non-geo neighbors around a selected geo node on the map.
 */
export function computeOrbitPosition(
  centerLat: number,
  centerLng: number,
  index: number,
  count: number,
  radiusDeg: number,
): { lat: number; lng: number } {
  const angle = (2 * Math.PI * index) / Math.max(count, 1) - Math.PI / 2;
  return {
    lat: centerLat + radiusDeg * Math.sin(angle),
    lng: centerLng + radiusDeg * Math.cos(angle),
  };
}

/**
 * Build a label matcher for the Sigma nodeReducer.
 * Delegates to the shared `buildStringMatcher` so label-level dimming
 * stays consistent with the data-level filtering in `filterGraph`.
 */
export function buildLabelMatcher(
  query: string,
  options: SearchOptions,
): (label: string) => boolean {
  return buildStringMatcher(query, options);
}

/**
 * Return ALL direct neighbors of `nodeId`, ignoring predicate filters.
 *
 * Used in map mode to determine which logical nodes should be *revealed*
 * when a geo node is selected — every related node becomes visible
 * regardless of which predicate filters are active.
 */
export function getFullNeighborSet(
  graph: Graph,
  nodeId: string,
): Set<string> {
  const neighbors = new Set<string>();
  if (!graph.hasNode(nodeId)) return neighbors;
  graph.forEachNeighbor(nodeId, (neighbor) => {
    neighbors.add(neighbor);
  });
  return neighbors;
}

/**
 * Check whether a non-geo node should be visible in map mode.
 *
 * Non-geo nodes are hidden by default on the map (they have no geographic
 * position and pile up at the village center). However, when a physical
 * (geo) node is selected, its direct neighbors are revealed so the user
 * can explore relationships on the map.
 */
export function isNonGeoVisibleOnMap(
  nodeId: string,
  selectedNodeId: string | null,
  selectedNeighbors: Set<string>,
): boolean {
  if (!selectedNodeId) return false;
  return selectedNeighbors.has(nodeId);
}

/** Check if an edge touches the given node. */
export function edgeTouchesNode(
  graph: Graph,
  edge: string,
  nodeId: string | null,
): boolean {
  if (!nodeId) return false;
  return graph.source(edge) === nodeId || graph.target(edge) === nodeId;
}

