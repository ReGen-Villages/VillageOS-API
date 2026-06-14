import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import {
  computePredicateStats,
  computeClusters,
  pickClusterRepresentatives,
} from './predicateCluster';
import { resolvePredicateColor } from './colors';

// ── Helpers ──────────────────────────────────────────────────────────────

function makeGraph(): Graph {
  const graph = new Graph({ multi: true, type: 'directed' });

  // Nodes
  graph.addNode('a', { label: 'A', thingType: 'default' });
  graph.addNode('b', { label: 'B', thingType: 'default' });
  graph.addNode('c', { label: 'C', thingType: 'default' });
  graph.addNode('d', { label: 'D', thingType: 'default' });
  graph.addNode('pred-has', { label: 'has', thingType: 'predicate' });

  // Edges with predicateId
  graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p-has', label: 'has' });
  graph.addDirectedEdgeWithKey('e2', 'b', 'c', { predicateId: 'p-has', label: 'has' });
  graph.addDirectedEdgeWithKey('e3', 'a', 'd', { predicateId: 'p-monitors', label: 'monitors' });
  graph.addDirectedEdgeWithKey('e4', 'c', 'a', { predicateId: 'p-is', label: 'is' });

  return graph;
}

// ── Tests ────────────────────────────────────────────────────────────────

describe('computePredicateStats', () => {
  it('counts edges per predicate', () => {
    const graph = makeGraph();
    const stats = computePredicateStats(graph);

    const hasStats = stats.find((s) => s.predicateId === 'p-has');
    expect(hasStats).toBeDefined();
    expect(hasStats!.edgeCount).toBe(2);

    const monStats = stats.find((s) => s.predicateId === 'p-monitors');
    expect(monStats).toBeDefined();
    expect(monStats!.edgeCount).toBe(1);
  });

  it('sorts by descending edge count', () => {
    const graph = makeGraph();
    const stats = computePredicateStats(graph);

    // "has" has 2 edges, should be first
    expect(stats[0].predicateId).toBe('p-has');
    expect(stats[0].edgeCount).toBe(2);
  });

  it('assigns colors via resolvePredicateColor (hash fallback)', () => {
    const graph = makeGraph();
    const stats = computePredicateStats(graph);

    // Each predicate gets its hash-based color
    const hasStats = stats.find((s) => s.predicateName === 'has');
    expect(hasStats!.color).toBe(resolvePredicateColor('has', {}));
    const monStats = stats.find((s) => s.predicateName === 'monitors');
    expect(monStats!.color).toBe(resolvePredicateColor('monitors', {}));
  });

  it('uses explicit overrides when provided', () => {
    const graph = makeGraph();
    const overrides = { has: '#ff0000', monitors: '#00ff00' };
    const stats = computePredicateStats(graph, overrides);

    const hasStats = stats.find((s) => s.predicateName === 'has');
    expect(hasStats!.color).toBe('#ff0000');
    const monStats = stats.find((s) => s.predicateName === 'monitors');
    expect(monStats!.color).toBe('#00ff00');
  });

  it('falls back to hash for predicates not in overrides', () => {
    const graph = makeGraph();
    const overrides = { has: '#ff0000' }; // monitors not overridden
    const stats = computePredicateStats(graph, overrides);

    const monStats = stats.find((s) => s.predicateName === 'monitors');
    expect(monStats!.color).toBe(resolvePredicateColor('monitors', {}));
  });

  it('uses edge label as predicate name', () => {
    const graph = makeGraph();
    const stats = computePredicateStats(graph);

    const hasStats = stats.find((s) => s.predicateId === 'p-has');
    expect(hasStats!.predicateName).toBe('has');
  });

  it('returns empty array for graph with no edges', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('x', {});
    expect(computePredicateStats(graph)).toEqual([]);
  });
});

describe('computeClusters', () => {
  it('finds connected components via selected predicates', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

    // a -has-> b -has-> c forms one component {a, b, c}
    // d is only connected via p-monitors (not selected) → unclustered
    expect(result.clusters.length).toBeGreaterThanOrEqual(1);

    const mainCluster = result.clusters.find(
      (s) => s.has('a') && s.has('b') && s.has('c'),
    );
    expect(mainCluster).toBeDefined();
    expect(mainCluster!.size).toBe(3);
  });

  it('marks nodes not in any cluster as -1', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

    // d is connected to a only via p-monitors → unclustered
    expect(result.nodeCluster.get('d')).toBe(-1);
  });

  it('excludes predicate-type nodes from clusters', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

    // pred-has is a predicate node → should not appear in any cluster
    expect(result.nodeCluster.get('pred-has')).toBe(-1);
    for (const cluster of result.clusters) {
      expect(cluster.has('pred-has')).toBe(false);
    }
  });

  it('creates separate clusters for disconnected components', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('x', { thingType: 'default' });
    graph.addNode('y', { thingType: 'default' });
    graph.addNode('z', { thingType: 'default' });
    graph.addDirectedEdgeWithKey('e-xy', 'x', 'y', { predicateId: 'p1' });
    // z is isolated → separate component or unclustered

    const result = computeClusters(graph, new Set(['p1']));
    expect(result.clusters.length).toBe(1);
    expect(result.clusters[0].has('x')).toBe(true);
    expect(result.clusters[0].has('y')).toBe(true);
    expect(result.nodeCluster.get('z')).toBe(-1);
  });

  it('supports union of multiple predicates', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has', 'p-monitors']));

    // a -has-> b, b -has-> c, a -monitors-> d → all in one component
    const mainCluster = result.clusters.find(
      (s) => s.has('a') && s.has('b') && s.has('c') && s.has('d'),
    );
    expect(mainCluster).toBeDefined();
  });

  it('picks representatives', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

    // Representatives should be picked for each cluster
    expect(result.representatives.size).toBe(result.clusters.length);
  });
});

describe('pickClusterRepresentatives', () => {
  it('selects highest-degree node per cluster', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('hub', {});
    graph.addNode('leaf1', {});
    graph.addNode('leaf2', {});
    graph.addDirectedEdgeWithKey('e1', 'hub', 'leaf1', {});
    graph.addDirectedEdgeWithKey('e2', 'hub', 'leaf2', {});

    const clusters = [new Set(['hub', 'leaf1', 'leaf2'])];
    const reps = pickClusterRepresentatives(graph, clusters);

    expect(reps.get(0)).toBe('hub'); // degree 2 vs 1
  });

  it('handles single-node clusters', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('solo', {});

    const clusters = [new Set(['solo'])];
    const reps = pickClusterRepresentatives(graph, clusters);

    expect(reps.get(0)).toBe('solo');
  });

  it('handles empty clusters array', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    const reps = pickClusterRepresentatives(graph, []);
    expect(reps.size).toBe(0);
  });

  it('skips empty cluster set (no bestNode)', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('a', {});
    // Pass a cluster with nodes not in the graph — iteration yields nothing
    const clusters = [new Set<string>()];
    const reps = pickClusterRepresentatives(graph, clusters);
    // Empty cluster → bestNode never set → no representative
    expect(reps.has(0)).toBe(false);
  });
});

describe('computePredicateStats — edge cases', () => {
  it('falls back to predicateId when edge label is missing', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('a', {});
    graph.addNode('b', {});
    graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p-feed', label: '' });

    const stats = computePredicateStats(graph);
    expect(stats).toHaveLength(1);
    // Empty label → falls back to predicateId
    expect(stats[0].predicateName).toBe('p-feed');
  });
});

describe('computeClusters — edge cases', () => {
  it('skips edges where source is a predicate-type node', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('pred', { thingType: 'predicate' });
    graph.addNode('a', { thingType: 'default' });
    graph.addNode('b', { thingType: 'default' });
    // Edge FROM predicate node
    graph.addDirectedEdgeWithKey('e1', 'pred', 'a', { predicateId: 'p1' });
    // Normal edge
    graph.addDirectedEdgeWithKey('e2', 'a', 'b', { predicateId: 'p1' });

    const result = computeClusters(graph, new Set(['p1']));
    // pred should not be in any cluster
    expect(result.nodeCluster.get('pred')).toBe(-1);
    // a and b should be clustered together
    const cluster = result.clusters.find((c) => c.has('a') && c.has('b'));
    expect(cluster).toBeDefined();
  });

  it('handles node with no neighbors in adjacency (BFS edge case)', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('a', { thingType: 'default' });
    graph.addNode('b', { thingType: 'default' });
    graph.addNode('isolated', { thingType: 'default' });
    graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p1' });
    // 'isolated' has no edges matching p1

    const result = computeClusters(graph, new Set(['p1']));
    expect(result.nodeCluster.get('isolated')).toBe(-1);
    expect(result.clusters.find((c) => c.has('a') && c.has('b'))).toBeDefined();
  });
});
