import type Graph from 'graphology';

interface ReconcileResult {
  nodesAdded: number;
  nodesRemoved: number;
  edgesAdded: number;
  edgesRemoved: number;
}

/**
 * Existing nodes keep their x and y, so a running layout and the camera are not disturbed, and edges are keyed
 * by relationship identifier, so several between one pair reconcile apart. Removals go through one `clear`
 * and `import`, because Sigma re-indexes the whole graph on every drop, and dropping one at a time freezes the
 * page when a filter hides much of a large model; adding costs the same per element whatever the graph's size.
 * `next` is changed on that path and must not be reused.
 */
export function reconcileGraph(current: Graph, next: Graph): ReconcileResult {
  const result: ReconcileResult = { nodesAdded: 0, nodesRemoved: 0, edgesAdded: 0, edgesRemoved: 0 };

  for (const node of current.nodes()) if (!next.hasNode(node)) result.nodesRemoved++;
  for (const edge of current.edges()) if (!next.hasEdge(edge)) result.edgesRemoved++;
  next.forEachNode((node) => { if (!current.hasNode(node)) result.nodesAdded++; });
  next.forEachEdge((edge) => { if (!current.hasEdge(edge)) result.edgesAdded++; });

  if (result.nodesRemoved > 0 || result.edgesRemoved > 0) {
    replaceWithTarget(current, next);
  } else {
    upsertFromTarget(current, next);
  }

  return result;
}

/** Survivors keep their laid-out x and y, and any attribute the rebuild does not know about, such as the
 *  `fixed` flags clustering pins nodes with. */
function replaceWithTarget(current: Graph, next: Graph): void {
  next.forEachNode((node, attributes) => {
    if (!current.hasNode(node)) return;
    const settled = current.getNodeAttributes(node);
    next.replaceNodeAttributes(node, { ...settled, ...attributes, x: settled.x, y: settled.y });
  });

  next.forEachEdge((edge, attributes) => {
    if (!current.hasEdge(edge)) return;
    next.replaceEdgeAttributes(edge, { ...current.getEdgeAttributes(edge), ...attributes });
  });

  current.clear();
  current.import(next);
}

/** Nothing was removed, so every element can be added or patched where it stands. */
function upsertFromTarget(current: Graph, next: Graph): void {
  next.forEachNode((node, attributes) => {
    if (current.hasNode(node)) {
      for (const [key, value] of Object.entries(attributes)) {
        if (key !== 'x' && key !== 'y') current.setNodeAttribute(node, key, value);
      }
    } else {
      current.addNode(node, attributes);
    }
  });

  next.forEachEdge((edge, attributes, source, target) => {
    if (current.hasEdge(edge)) {
      for (const [key, value] of Object.entries(attributes)) {
        current.setEdgeAttribute(edge, key, value);
      }
    } else {
      current.addDirectedEdgeWithKey(edge, source, target, attributes);
    }
  });
}
