import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import { reconcileGraph } from './graphReconcile';

function node(id: string, extra: Record<string, unknown> = {}) {
  return { x: 0, y: 0, size: 5, color: '#fff', label: id, ...extra };
}

/** Directed multigraph, matching how buildGraph constructs its graphs. */
function makeGraph(): Graph {
  return new Graph({ multi: true, type: 'directed' });
}

describe('reconcileGraph', () => {
  it('adds nodes and edges present only in the target', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1'));

    const next = makeGraph();
    next.addNode('n1', node('n1'));
    next.addNode('n2', node('n2'));
    next.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1, label: 'is' });

    const result = reconcileGraph(current, next);

    expect(current.hasNode('n2')).toBe(true);
    expect(current.hasEdge('e1')).toBe(true);
    expect(result).toMatchObject({ nodesAdded: 1, edgesAdded: 1, nodesRemoved: 0, edgesRemoved: 0 });
  });

  it('drops an edge whose endpoints both remain', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1'));
    current.addNode('n2', node('n2'));
    current.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1 });

    // Both nodes stay; only the relationship is gone.
    const next = makeGraph();
    next.addNode('n1', node('n1'));
    next.addNode('n2', node('n2'));

    const result = reconcileGraph(current, next);

    expect(current.hasNode('n1')).toBe(true);
    expect(current.hasNode('n2')).toBe(true);
    expect(current.hasEdge('e1')).toBe(false);
    expect(result).toMatchObject({ nodesRemoved: 0, edgesRemoved: 1, nodesAdded: 0, edgesAdded: 0 });
  });

  it('preserves existing node x/y while patching other attributes', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1', { x: 42, y: -17, size: 5, color: '#111' }));

    const next = makeGraph();
    // Target rebuilds n1 at its seed position (0,0) with a new size/color.
    next.addNode('n1', node('n1', { x: 0, y: 0, size: 12, color: '#999' }));

    reconcileGraph(current, next);

    expect(current.getNodeAttribute('n1', 'x')).toBe(42);
    expect(current.getNodeAttribute('n1', 'y')).toBe(-17);
    expect(current.getNodeAttribute('n1', 'size')).toBe(12);
    expect(current.getNodeAttribute('n1', 'color')).toBe('#999');
  });

  it('patches existing edge attributes in place', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1'));
    current.addNode('n2', node('n2'));
    current.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1, color: '#111' });

    const next = makeGraph();
    next.addNode('n1', node('n1'));
    next.addNode('n2', node('n2'));
    next.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 3, color: '#999' });

    const result = reconcileGraph(current, next);

    expect(current.getEdgeAttribute('e1', 'size')).toBe(3);
    expect(current.getEdgeAttribute('e1', 'color')).toBe('#999');
    expect(result.edgesAdded).toBe(0);
  });

  it('is a no-op when the graphs are already identical', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1'));
    current.addNode('n2', node('n2'));
    current.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1 });

    const next = makeGraph();
    next.addNode('n1', node('n1'));
    next.addNode('n2', node('n2'));
    next.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1 });

    const result = reconcileGraph(current, next);

    expect(result).toEqual({ nodesAdded: 0, nodesRemoved: 0, edgesAdded: 0, edgesRemoved: 0 });
  });

  it('drops a node and its incident edges together', () => {
    const current = makeGraph();
    current.addNode('n1', node('n1'));
    current.addNode('n2', node('n2'));
    current.addDirectedEdgeWithKey('e1', 'n1', 'n2', { size: 1 });

    // Target keeps only n1 — n2 and the edge that referenced it are gone.
    const next = makeGraph();
    next.addNode('n1', node('n1'));

    const result = reconcileGraph(current, next);

    expect(current.order).toBe(1);
    expect(current.size).toBe(0);
    expect(result.nodesRemoved).toBe(1);
    // The incident edge is removed by dropNode, so it is not double-counted.
    expect(result.edgesRemoved).toBe(0);
  });
});
