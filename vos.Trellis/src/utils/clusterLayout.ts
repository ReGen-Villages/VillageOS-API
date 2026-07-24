import type Graph from 'graphology';

/**
 * Layout primitives that let predicate clustering run inside the off-thread
 * ForceAtlas2 engine, which has no shouldSkipNode/shouldSkipEdge callbacks.
 *
 * FA2 reads a node's `fixed` attribute and an edge-weight getter on the main
 * thread when it builds the layout matrices, so pinning the background with
 * `fixed` and zeroing non-active edge weights reproduces the old force-engine
 * clustering behaviour without a per-frame main-thread simulation.
 */

/** Nodes touched by at least one edge whose predicate is in the active set. */
export function collectClusterNodeIds(
  graph: Graph,
  activePredicateIds: Set<string>,
): Set<string> {
  const nodeIds = new Set<string>();
  graph.forEachEdge((_edge, attrs, source, target) => {
    if (activePredicateIds.has(attrs.predicateId as string)) {
      nodeIds.add(source);
      nodeIds.add(target);
    }
  });
  return nodeIds;
}

/**
 * Pin every node that is not a cluster member so only the active-predicate
 * members move; cluster members are freed. Mirrors the old shouldSkipNode.
 */
export function applyClusterFixedFlags(graph: Graph, clusterNodeIds: Set<string>): void {
  graph.forEachNode((node) => {
    graph.setNodeAttribute(node, 'fixed', !clusterNodeIds.has(node));
  });
}

/** Release every node so the whole graph is free to move again. */
export function clearFixedFlags(graph: Graph): void {
  graph.forEachNode((node) => {
    graph.setNodeAttribute(node, 'fixed', false);
  });
}

/**
 * Edge-weight getter for FA2: active-predicate edges pull (weight 1), all
 * others exert no attraction (weight 0). Mirrors the old shouldSkipEdge.
 */
export function makeActivePredicateWeightGetter(
  activePredicateIds: Set<string>,
): (edge: string, attrs: Record<string, unknown>) => number {
  return (_edge, attrs) =>
    activePredicateIds.has(attrs.predicateId as string) ? 1 : 0;
}
