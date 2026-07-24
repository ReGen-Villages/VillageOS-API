import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import {
  collectClusterNodeIds,
  applyClusterFixedFlags,
  clearFixedFlags,
  makeActivePredicateWeightGetter,
} from './clusterLayout';

function makeGraph(): Graph {
  const graph = new Graph({ multi: true, type: 'directed' });
  graph.addNode('a', { label: 'A' });
  graph.addNode('b', { label: 'B' });
  graph.addNode('c', { label: 'C' });
  graph.addNode('d', { label: 'D' });
  graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p-has' });
  graph.addDirectedEdgeWithKey('e2', 'b', 'c', { predicateId: 'p-has' });
  graph.addDirectedEdgeWithKey('e3', 'a', 'd', { predicateId: 'p-monitors' });
  return graph;
}

describe('collectClusterNodeIds', () => {
  it('returns only endpoints of edges whose predicate is active', () => {
    const graph = makeGraph();
    const ids = collectClusterNodeIds(graph, new Set(['p-has']));
    expect([...ids].sort()).toEqual(['a', 'b', 'c']);
    expect(ids.has('d')).toBe(false);
  });

  it('returns an empty set when no edge matches', () => {
    const graph = makeGraph();
    expect(collectClusterNodeIds(graph, new Set(['p-none'])).size).toBe(0);
  });
});

describe('applyClusterFixedFlags', () => {
  it('pins every node NOT in the cluster and frees cluster members', () => {
    const graph = makeGraph();
    applyClusterFixedFlags(graph, new Set(['a', 'b', 'c']));
    expect(graph.getNodeAttribute('a', 'fixed')).toBe(false);
    expect(graph.getNodeAttribute('b', 'fixed')).toBe(false);
    expect(graph.getNodeAttribute('c', 'fixed')).toBe(false);
    // 'd' is not a cluster member → pinned so the background holds still.
    expect(graph.getNodeAttribute('d', 'fixed')).toBe(true);
  });
});

describe('clearFixedFlags', () => {
  it('frees every node so the whole graph can move again', () => {
    const graph = makeGraph();
    applyClusterFixedFlags(graph, new Set(['a']));
    clearFixedFlags(graph);
    graph.forEachNode((_node, attrs) => {
      expect(attrs.fixed).toBe(false);
    });
  });
});

describe('makeActivePredicateWeightGetter', () => {
  it('gives active-predicate edges weight 1 and everything else weight 0', () => {
    const getWeight = makeActivePredicateWeightGetter(new Set(['p-has']));
    expect(getWeight('e1', { predicateId: 'p-has' })).toBe(1);
    expect(getWeight('e3', { predicateId: 'p-monitors' })).toBe(0);
  });
});
