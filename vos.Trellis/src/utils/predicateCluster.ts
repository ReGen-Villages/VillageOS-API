import type Graph from 'graphology';
import { resolvePredicateColor } from './colors';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';

export interface PredicateStatistics {
  predicateId: string;
  predicateName: string;
  edgeCount: number;
  color: string;
}

export interface ClusterMap {
  /** Maps node ID → cluster index. Nodes not in any cluster get -1. */
  nodeCluster: Map<string, number>;
  /** Each set is one group of nodes joined by the chosen predicates. */
  clusters: Set<string>[];
  /** The node with the most relationships in each cluster. */
  representatives: Map<number, string>;
}

/** Counted over the whole model rather than the drawn graph, which the map view thins, so every
 *  predicate still appears in the radial menu. */
export function computePredicateStatisticsFromModel(
  things: VosThing[],
  relationships: VosRelationship[],
  predicateColors: Record<string, string> = {},
): PredicateStatistics[] {
  const thingNames = new Map(things.map((t) => [t.Id, t.Name]));
  const counts = new Map<string, { name: string; count: number }>();

  for (const relationship of relationships) {
    const predicateId = relationship.PredicateId;
    const existing = counts.get(predicateId);
    if (existing) {
      existing.count++;
    } else {
      const name = thingNames.get(predicateId) ?? predicateId;
      counts.set(predicateId, { name, count: 1 });
    }
  }

  return [...counts.entries()]
    .map(([predicateId, { name, count }]) => ({
      predicateId,
      predicateName: name,
      edgeCount: count,
      color: resolvePredicateColor(name, predicateColors),
    }))
    .sort((a, b) => b.edgeCount - a.edgeCount);
}

/** Groups joined by any of the given predicates. Predicate nodes are left out, because they only connect others. */
export function computeClusters(graph: Graph, predicateIds: Set<string>): ClusterMap {
  const adj = new Map<string, Set<string>>();

  graph.forEachEdge((_edge, attributes, source, target) => {
    if (!predicateIds.has(attributes.predicateId as string)) return;

    const sourceType = graph.getNodeAttribute(source, 'thingType');
    const targetType = graph.getNodeAttribute(target, 'thingType');
    if (sourceType === 'predicate' || targetType === 'predicate') return;

    if (!adj.has(source)) adj.set(source, new Set());
    if (!adj.has(target)) adj.set(target, new Set());
    adj.get(source)!.add(target);
    adj.get(target)!.add(source);
  });

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

  graph.forEachNode((node, _attributes) => {
    if (!nodeCluster.has(node)) {
      nodeCluster.set(node, -1);
    }
  });

  const representatives = pickClusterRepresentatives(graph, clusters);

  return { nodeCluster, clusters, representatives };
}

/** When a cluster is collapsed, only its representative stays visible. */
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
