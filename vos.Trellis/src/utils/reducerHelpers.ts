import type { ClusterMap } from './predicateCluster';
import type { FlashSettings } from './guiSettings';
import { brightenColor } from './colors';

// ── Constants ────────────────────────────────────────────────────────────

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
  _isMapOn: boolean,
): Record<string, unknown> {
  const thingType = data.thingType as string | undefined;

  if (thingType === 'predicate') {
    return applySelectionHighlight(node, {
      ...data, color: '#3f3f46', size: 2, label: '', zIndex: 0,
    }, selId);
  }

  if (expandedNodes.has(node)) {
    return applySelectionHighlight(node, { ...data, zIndex: 2 }, selId);
  }

  const clusterIndex = clusterMap.nodeCluster.get(node);

  if (clusterIndex === undefined || clusterIndex === -1) {
    return applySelectionHighlight(node, {
      ...data, color: '#27272a', size: 2, label: '', zIndex: 0,
    }, selId);
  }

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
