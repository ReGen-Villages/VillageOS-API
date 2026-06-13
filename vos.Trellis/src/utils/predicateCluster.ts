import type Graph from 'graphology';
import { resolvePredicateColor } from './colors';
import type { VosThing, VosRelationship } from '../types/vos';

// ── Types ─────────────────────────────────────────────────────────────

export interface PredicateStats {
  predicateId: string;
  predicateName: string;
  edgeCount: number;
  color: string;
}

export interface ClusterMap {
  /** Maps node ID → cluster index. Nodes not in any cluster get -1. */
  nodeCluster: Map<string, number>;
  /** Array of sets; each set contains the node IDs forming a connected component via the predicate. */
  clusters: Set<string>[];
  /** Representative node ID per cluster (highest-degree node). */
  representatives: Map<number, string>;
}

// ── Predicate statistics ──────────────────────────────────────────────

/** Convert an accumulated counts map into a sorted PredicateStats array. */
function buildPredicateStatsArray(
  counts: Map<string, { name: string; count: number }>,
  predicateColors: Record<string, string>,
): PredicateStats[] {
  return [...counts.entries()]
    .map(([predicateId, { name, count }]) => ({
      predicateId,
      predicateName: name,
      edgeCount: count,
      color: resolvePredicateColor(name, predicateColors),
    }))
    .sort((a, b) => b.edgeCount - a.edgeCount);
}

/**
 * Count edges per predicate and return sorted (most-used first).
 * Colors are resolved via `resolvePredicateColor` — explicit overrides
 * from GUI_Settings take priority, with hash-based fallback.
 */
export function computePredicateStats(
  graph: Graph,
  predicateColors: Record<string, string> = {},
): PredicateStats[] {
  const counts = new Map<string, { name: string; count: number }>();

  graph.forEachEdge((_edge, attrs) => {
    const pid = attrs.predicateId as string;
    const label = attrs.label as string;
    const existing = counts.get(pid);
    if (existing) {
      existing.count++;
    } else {
      counts.set(pid, { name: label || pid, count: 1 });
    }
  });

  return buildPredicateStatsArray(counts, predicateColors);
}

/**
 * Count edges per predicate from the full model store data (unfiltered).
 * Unlike computePredicateStats which uses the Sigma graph (which may be
 * filtered by map-mode surface reduction), this operates on all relationships
 * so predicates like consumes/produces always appear in the radial menu.
 */
export function computePredicateStatsFromModel(
  things: VosThing[],
  relationships: VosRelationship[],
  predicateColors: Record<string, string> = {},
): PredicateStats[] {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const counts = new Map<string, { name: string; count: number }>();

  for (const rel of relationships) {
    const pid = rel.PredicateId;
    const existing = counts.get(pid);
    if (existing) {
      existing.count++;
    } else {
      const name = thingNames.get(pid) ?? pid;
      counts.set(pid, { name, count: 1 });
    }
  }

  return buildPredicateStatsArray(counts, predicateColors);
}

// ── Cluster computation via BFS ───────────────────────────────────────

/**
 * Find connected components in the graph using only edges whose predicateId
 * is in the given `predicateIds` set (union semantics).
 * Predicate-type nodes (nodes whose thingType === 'predicate') are excluded from clusters.
 */
export function computeClusters(graph: Graph, predicateIds: Set<string>): ClusterMap {
  // Build adjacency list for the chosen predicates (union of edges)
  const adj = new Map<string, Set<string>>();

  graph.forEachEdge((_edge, attrs, source, target) => {
    if (!predicateIds.has(attrs.predicateId as string)) return;

    // Skip predicate-type nodes (they are structural connectors)
    const sourceType = graph.getNodeAttribute(source, 'thingType');
    const targetType = graph.getNodeAttribute(target, 'thingType');
    if (sourceType === 'predicate' || targetType === 'predicate') return;

    if (!adj.has(source)) adj.set(source, new Set());
    if (!adj.has(target)) adj.set(target, new Set());
    adj.get(source)!.add(target);
    adj.get(target)!.add(source);
  });

  // BFS to find connected components
  const visited = new Set<string>();
  const clusters: Set<string>[] = [];
  const nodeCluster = new Map<string, number>();

  for (const startNode of adj.keys()) {
    if (visited.has(startNode)) continue;

    const component = new Set<string>();
    const queue = [startNode];
    visited.add(startNode);

    while (queue.length > 0) {
      const node = queue.shift()!;
      component.add(node);

      const neighbors = adj.get(node);
      if (neighbors) {
        for (const neighbor of neighbors) {
          if (!visited.has(neighbor)) {
            visited.add(neighbor);
            queue.push(neighbor);
          }
        }
      }
    }

    const clusterIndex = clusters.length;
    clusters.push(component);
    for (const node of component) {
      nodeCluster.set(node, clusterIndex);
    }
  }

  // Mark all other nodes as unclustered (-1)
  graph.forEachNode((node, _attrs) => {
    if (!nodeCluster.has(node)) {
      nodeCluster.set(node, -1);
    }
  });

  // Pick representatives
  const representatives = pickClusterRepresentatives(graph, clusters);

  return { nodeCluster, clusters, representatives };
}

// ── Representative selection ──────────────────────────────────────────

/**
 * Pick the highest-degree node in each cluster as its representative.
 * When a cluster is collapsed, only the representative remains visible.
 */
export function pickClusterRepresentatives(
  graph: Graph,
  clusters: Set<string>[],
): Map<number, string> {
  const reps = new Map<number, string>();

  for (let i = 0; i < clusters.length; i++) {
    let bestNode = '';
    let bestDegree = -1;

    for (const node of clusters[i]) {
      const degree = graph.degree(node);
      if (degree > bestDegree) {
        bestDegree = degree;
        bestNode = node;
      }
    }

    if (bestNode) reps.set(i, bestNode);
  }

  return reps;
}
