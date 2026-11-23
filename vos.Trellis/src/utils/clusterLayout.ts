import type Graph from 'graphology';

/** The ForceAtlas2 worker cannot be told to skip a node or edge, but it reads each node's `fixed` attribute
 *  and the edge weights when it starts, so pinning the rest and zeroing other edges clusters the active
 *  members without a simulation on the main thread every frame. */

/** Nodes touched by at least one edge whose predicate is in the active set. */
export function collectClusterNodeIds(
  graph: Graph,
  activePredicateIds: Set<string>,
): Set<string> {
  const nodeIds = new Set<string>();
  graph.forEachEdge((_edge, attributes, source, target) => {
    if (activePredicateIds.has(attributes.predicateId as string)) {
      nodeIds.add(source);
      nodeIds.add(target);
    }
  });
  return nodeIds;
}

/** Pin every node that is not a cluster member so only the active-predicate members move. */
export function applyClusterFixedFlags(graph: Graph, clusterNodeIds: Set<string>): void {
  graph.forEachNode((node) => {
    graph.setNodeAttribute(node, 'fixed', !clusterNodeIds.has(node));
  });
}

export function clearFixedFlags(graph: Graph): void {
  graph.forEachNode((node) => {
    graph.setNodeAttribute(node, 'fixed', false);
  });
}

/** Active-predicate edges pull (weight 1); all others exert no attraction (weight 0). */
export function makeActivePredicateWeightGetter(
  activePredicateIds: Set<string>,
): (edge: string, attributes: Record<string, unknown>) => number {
  return (_edge, attributes) =>
    activePredicateIds.has(attributes.predicateId as string) ? 1 : 0;
}
