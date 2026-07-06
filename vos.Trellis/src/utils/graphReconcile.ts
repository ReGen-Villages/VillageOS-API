import type Graph from 'graphology';

export interface ReconcileResult {
  nodesAdded: number;
  nodesRemoved: number;
  edgesAdded: number;
  edgesRemoved: number;
}

/**
 * Reconcile a live graphology graph toward a freshly built target, mutating in
 * place: add new nodes/edges, drop removed ones, and patch changed attributes.
 *
 * Existing nodes keep their current x/y, so a running force layout and the
 * camera are left undisturbed — only genuinely new elements get a seed position.
 * Edge identity is the relationship Id (the graphology edge key), so multi-edges
 * between the same pair reconcile independently.
 */
export function reconcileGraph(current: Graph, next: Graph): ReconcileResult {
  const result: ReconcileResult = { nodesAdded: 0, nodesRemoved: 0, edgesAdded: 0, edgesRemoved: 0 };

  // Drop nodes gone from the target. dropNode also removes their incident edges.
  for (const node of current.nodes()) {
    if (!next.hasNode(node)) {
      current.dropNode(node);
      result.nodesRemoved++;
    }
  }

  // Drop edges gone from the target (those that survived the node drops above).
  for (const edge of current.edges()) {
    if (!next.hasEdge(edge)) {
      current.dropEdge(edge);
      result.edgesRemoved++;
    }
  }

  // Upsert nodes: patch existing ones without touching x/y (preserve settled
  // positions); add new ones with the seed position the target assigned.
  next.forEachNode((node, attrs) => {
    if (current.hasNode(node)) {
      for (const [key, value] of Object.entries(attrs)) {
        if (key !== 'x' && key !== 'y') current.setNodeAttribute(node, key, value);
      }
    } else {
      current.addNode(node, attrs);
      result.nodesAdded++;
    }
  });

  // Upsert edges. Endpoints exist by now, so new edges can be added directly.
  next.forEachEdge((edge, attrs, source, target) => {
    if (current.hasEdge(edge)) {
      for (const [key, value] of Object.entries(attrs)) {
        current.setEdgeAttribute(edge, key, value);
      }
    } else {
      current.addDirectedEdgeWithKey(edge, source, target, attrs);
      result.edgesAdded++;
    }
  });

  return result;
}
