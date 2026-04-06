import type { ClusterMap } from './predicateCluster';
import type { FlashSettings } from './guiSettings';
import { isNonGeoVisibleOnMap } from './nodeVisibility';
import { brightenColor } from './colors';

// ── Constants ────────────────────────────────────────────────────────────

/**
 * Size threshold below which geo nodes are rendered semi-transparent on
 * the map. Nodes at or below this size (sensors, furniture, small points)
 * become translucent so larger building footprints remain visible.
 */
export const GEO_OPACITY_SIZE_THRESHOLD = 5;
export const GEO_SMALL_NODE_ALPHA = 0.45;

/** Bright style for edges connected to hovered or selected node. */
export const BRIGHT_EDGE_COLOR = '#d4d4d8'; // zinc-300
export const BRIGHT_EDGE_SIZE = 1.5;

// ── Pure helpers ─────────────────────────────────────────────────────────

/** Append an alpha channel to a hex colour string. */
export function withAlpha(hex: string, alpha: number): string {
  const a = Math.round(alpha * 255).toString(16).padStart(2, '0');
  if (hex.length === 7) return hex + a;
  if (hex.length === 9) return hex.slice(0, 7) + a;
  return hex;
}

/** Apply selection highlight to a node result. */
export function applySelectionHighlight(
  node: string,
  result: Record<string, unknown>,
  selId: string | null,
): Record<string, unknown> {
  if (node === selId) {
    return { ...result, highlighted: true, zIndex: 10 };
  }
  return result;
}

/** Return bright edge style for hovered/selected edges. */
export function brightenEdge(data: Record<string, unknown>): Record<string, unknown> {
  return { ...data, color: BRIGHT_EDGE_COLOR, size: BRIGHT_EDGE_SIZE, forceLabel: true };
}

/**
 * Apply semi-transparency to small geo nodes on the map.
 * Returns modified data if the node is small, or null if no change needed.
 */
export function applyGeoNodeOpacity(
  data: Record<string, unknown>,
): Record<string, unknown> | null {
  if (!data.hasGeometry) return null;
  const size = (data.size as number) || 3;
  if (size > GEO_OPACITY_SIZE_THRESHOLD) return null;
  const color = (data.color as string) || '#94a3b8';
  return { ...data, color: withAlpha(color, GEO_SMALL_NODE_ALPHA), zIndex: 0 };
}

/**
 * Determine visibility/style for a non-geo node in map mode.
 *
 * Returns:
 * - Object with `hidden: true` if the node should be hidden
 * - Object with revealed style if the node is a selection neighbor
 * - Object with small/dim style if it's a predicate-revealed endpoint
 * - `null` if the node has geometry (caller should handle geo nodes)
 */
export function resolveNonGeoMapVisibility(
  node: string,
  data: Record<string, unknown>,
  selId: string | null,
  fullNeighbors: Set<string>,
  predicateRevealedNodes?: Set<string>,
): Record<string, unknown> | null {
  if (data.hasGeometry) return null;

  const isRevealed = isNonGeoVisibleOnMap(node, selId, fullNeighbors);

  if (isRevealed) {
    return applySelectionHighlight(node, { ...data, zIndex: 2 }, selId);
  }
  if (predicateRevealedNodes && predicateRevealedNodes.has(node)) {
    // Show as small, dim node — orbital positioning handled by MaplibreLayer
    const color = (data.color as string) || '#94a3b8';
    return applySelectionHighlight(node, {
      ...data,
      color: withAlpha(color.length >= 7 ? color.slice(0, 7) : color, 0.6),
      zIndex: 1,
    }, selId);
  }
  return { ...data, hidden: true };
}

/**
 * Compute the visual style for a node in clustering mode.
 *
 * Handles: predicate nodes (dimmed), expanded nodes, unclustered nodes,
 * collapsed cluster members (hidden except representative), and normal
 * cluster members.
 */
export function computeClusterNodeStyle(
  node: string,
  data: Record<string, unknown>,
  clusterMap: ClusterMap,
  collapsedClusters: Set<number>,
  expandedNodes: Set<string>,
  selId: string | null,
  isMapOn: boolean,
): Record<string, unknown> {
  const thingType = data.thingType as string | undefined;

  // Predicate-type nodes are structural connectors — always dim
  if (thingType === 'predicate') {
    return applySelectionHighlight(node, {
      ...data, color: '#3f3f46', size: 2, label: '', zIndex: 0,
    }, selId);
  }

  // Node explicitly expanded via double-click
  if (expandedNodes.has(node)) {
    return applySelectionHighlight(node, { ...data, zIndex: 2 }, selId);
  }

  const clusterIndex = clusterMap.nodeCluster.get(node);

  // Unclustered node (not connected by active predicate)
  if (clusterIndex === undefined || clusterIndex === -1) {
    return applySelectionHighlight(node, {
      ...data, color: '#27272a', size: 2, label: '', zIndex: 0,
    }, selId);
  }

  // Node is in a collapsed cluster
  if (collapsedClusters.has(clusterIndex)) {
    const representative = clusterMap.representatives.get(clusterIndex);
    if (node === representative) {
      const clusterSize = clusterMap.clusters[clusterIndex]?.size || 1;
      return applySelectionHighlight(node, {
        ...data,
        size: Math.min(((data.size as number) || 5) * 1.5, 20),
        label: `${data.label || ''} (${clusterSize})`,
        zIndex: 2,
      }, selId);
    }
    return { ...data, hidden: true };
  }

  // Node is in an expanded cluster — apply small-geo opacity in map mode
  if (isMapOn) {
    const geoResult = applyGeoNodeOpacity(data);
    if (geoResult) return applySelectionHighlight(node, geoResult, selId);
  }

  return applySelectionHighlight(node, { ...data, zIndex: 1 }, selId);
}

/** Apply flash pulse to a node whose property just changed. */
export function applyNodeFlash(data: Record<string, unknown>, settings: FlashSettings): Record<string, unknown> {
  const color = (data.color as string) || '#94a3b8';
  const baseColor = color.length >= 7 ? color.slice(0, 7) : color;
  const size = (data.size as number) || 5;
  return {
    ...data,
    color: brightenColor(baseColor, settings.flashNodeBrighten),
    size: size * settings.flashNodeSizeFactor,
    zIndex: 10,
  };
}

/** Apply flash pulse to an edge whose property just changed. */
export function applyEdgeFlash(data: Record<string, unknown>, settings: FlashSettings): Record<string, unknown> {
  const color = (data.color as string) || '#71717a';
  const baseColor = color.length >= 7 ? color.slice(0, 7) : color;
  return { ...data, color: brightenColor(baseColor, settings.flashNodeBrighten), size: settings.flashEdgeSize, forceLabel: true };
}
