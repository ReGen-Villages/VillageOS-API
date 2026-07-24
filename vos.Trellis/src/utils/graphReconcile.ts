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
 *
 * Removals go through one `clear` + `import` instead of per-element drops.
 * Sigma re-indexes the whole graph synchronously on every `nodeDropped` /
 * `edgeDropped` event, so dropping one at a time costs O(removed × graph size)
 * and wedges the main thread when a filter hides a large share of a big model.
 * A single `cleared` event costs one re-index, and the re-import rides Sigma's
 * per-element add path, which is O(1) each.
 *
 * `next` is scratch space on that path — it is mutated and must not be reused.
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

/**
 * Fold the live graph's settled state into `next`, then swap the whole graph
 * over in one mutation. Survivors keep their laid-out x/y and any attribute the
 * live graph carries that the rebuild doesn't know about (the `fixed` flags
 * predicate clustering pins nodes with, for one).
 */
function replaceWithTarget(current: Graph, next: Graph): void {
  next.forEachNode((node, attrs) => {
    if (!current.hasNode(node)) return;
    const settled = current.getNodeAttributes(node);
    next.replaceNodeAttributes(node, { ...settled, ...attrs, x: settled.x, y: settled.y });
  });

  next.forEachEdge((edge, attrs) => {
    if (!current.hasEdge(edge)) return;
    next.replaceEdgeAttributes(edge, { ...current.getEdgeAttributes(edge), ...attrs });
  });

  current.clear();
  current.import(next);
}

/** Nothing was removed, so every element can be added or patched where it stands. */
function upsertFromTarget(current: Graph, next: Graph): void {
  next.forEachNode((node, attrs) => {
    if (current.hasNode(node)) {
      for (const [key, value] of Object.entries(attrs)) {
        if (key !== 'x' && key !== 'y') current.setNodeAttribute(node, key, value);
      }
    } else {
      current.addNode(node, attrs);
    }
  });

  next.forEachEdge((edge, attrs, source, target) => {
    if (current.hasEdge(edge)) {
      for (const [key, value] of Object.entries(attrs)) {
        current.setEdgeAttribute(edge, key, value);
      }
    } else {
      current.addDirectedEdgeWithKey(edge, source, target, attrs);
    }
  });
}
