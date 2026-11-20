import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';
import {
  computePredicateStatisticsFromModel,
  computeClusters,
  pickClusterRepresentatives,
} from './predicateCluster';
import { resolvePredicateColor } from './colors';

function thing(id: string, name: string): VosThing {
  return { Id: id, Name: name, Properties: {} };
}

function relationship(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: '', SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

function makeModel(): { things: VosThing[]; relationships: VosRelationship[] } {
  return {
    things: [
      thing('a', 'A'), thing('b', 'B'), thing('c', 'C'), thing('d', 'D'),
      thing('p-has', 'has'), thing('p-monitors', 'monitors'), thing('p-is', 'is'),
    ],
    relationships: [
      relationship('e1', 'a', 'p-has', 'b'),
      relationship('e2', 'b', 'p-has', 'c'),
      relationship('e3', 'a', 'p-monitors', 'd'),
      relationship('e4', 'c', 'p-is', 'a'),
    ],
  };
}

function statisticsOf(predicateColors?: Record<string, string>) {
  const { things, relationships } = makeModel();
  return computePredicateStatisticsFromModel(things, relationships, predicateColors);
}

function makeGraph(): Graph {
  const graph = new Graph({ multi: true, type: 'directed' });

  graph.addNode('a', { label: 'A', thingType: 'default' });
  graph.addNode('b', { label: 'B', thingType: 'default' });
  graph.addNode('c', { label: 'C', thingType: 'default' });
  graph.addNode('d', { label: 'D', thingType: 'default' });
  graph.addNode('pred-has', { label: 'has', thingType: 'predicate' });

  graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p-has', label: 'has' });
  graph.addDirectedEdgeWithKey('e2', 'b', 'c', { predicateId: 'p-has', label: 'has' });
  graph.addDirectedEdgeWithKey('e3', 'a', 'd', { predicateId: 'p-monitors', label: 'monitors' });
  graph.addDirectedEdgeWithKey('e4', 'c', 'a', { predicateId: 'p-is', label: 'is' });

  return graph;
}

describe('computePredicateStatisticsFromModel', () => {
  it('counts edges per predicate', () => {
    const statistics = statisticsOf();

    const hasStatistics = statistics.find((s) => s.predicateId === 'p-has');
    expect(hasStatistics).toBeDefined();
    expect(hasStatistics!.edgeCount).toBe(2);

    const monitorsStatistics = statistics.find((s) => s.predicateId === 'p-monitors');
    expect(monitorsStatistics).toBeDefined();
    expect(monitorsStatistics!.edgeCount).toBe(1);
  });

  it('sorts by descending edge count', () => {
    const statistics = statisticsOf();

    expect(statistics[0].predicateId).toBe('p-has');
    expect(statistics[0].edgeCount).toBe(2);
  });

  it('assigns colors via resolvePredicateColor (hash fallback)', () => {
    const statistics = statisticsOf();

    const hasStatistics = statistics.find((s) => s.predicateName === 'has');
    expect(hasStatistics!.color).toBe(resolvePredicateColor('has', {}));
    const monitorsStatistics = statistics.find((s) => s.predicateName === 'monitors');
    expect(monitorsStatistics!.color).toBe(resolvePredicateColor('monitors', {}));
  });

  it('uses explicit overrides when provided', () => {
    const statistics = statisticsOf({ has: '#ff0000', monitors: '#00ff00' });

    const hasStatistics = statistics.find((s) => s.predicateName === 'has');
    expect(hasStatistics!.color).toBe('#ff0000');
    const monitorsStatistics = statistics.find((s) => s.predicateName === 'monitors');
    expect(monitorsStatistics!.color).toBe('#00ff00');
  });

  it('falls back to hash for predicates not in overrides', () => {
    const statistics = statisticsOf({ has: '#ff0000' });

    const monitorsStatistics = statistics.find((s) => s.predicateName === 'monitors');
    expect(monitorsStatistics!.color).toBe(resolvePredicateColor('monitors', {}));
  });

  it('names each predicate by its Thing', () => {
    const statistics = statisticsOf();

    const hasStatistics = statistics.find((s) => s.predicateId === 'p-has');
    expect(hasStatistics!.predicateName).toBe('has');
  });

  it('returns empty array for a model with no relationships', () => {
    expect(computePredicateStatisticsFromModel([thing('x', 'X')], [])).toEqual([]);
  });

  it('falls back to predicateId when the predicate Thing is not in the model', () => {
    const statistics = computePredicateStatisticsFromModel([thing('a', 'A'), thing('b', 'B')], [relationship('e1', 'a', 'p-feed', 'b')]);
    expect(statistics).toHaveLength(1);
    expect(statistics[0].predicateName).toBe('p-feed');
  });
});

describe('computeClusters', () => {
  it('finds connected components via selected predicates', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

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

    expect(result.nodeCluster.get('d')).toBe(-1);
  });

  it('excludes predicate-type nodes from clusters', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

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

    const result = computeClusters(graph, new Set(['p1']));
    expect(result.clusters.length).toBe(1);
    expect(result.clusters[0].has('x')).toBe(true);
    expect(result.clusters[0].has('y')).toBe(true);
    expect(result.nodeCluster.get('z')).toBe(-1);
  });

  it('supports union of multiple predicates', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has', 'p-monitors']));

    const mainCluster = result.clusters.find(
      (s) => s.has('a') && s.has('b') && s.has('c') && s.has('d'),
    );
    expect(mainCluster).toBeDefined();
  });

  it('picks representatives', () => {
    const graph = makeGraph();
    const result = computeClusters(graph, new Set(['p-has']));

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
    const clusters = [new Set<string>()];
    const reps = pickClusterRepresentatives(graph, clusters);
    expect(reps.has(0)).toBe(false);
  });
});

describe('computeClusters — edge cases', () => {
  it('skips edges where source is a predicate-type node', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('pred', { thingType: 'predicate' });
    graph.addNode('a', { thingType: 'default' });
    graph.addNode('b', { thingType: 'default' });
    graph.addDirectedEdgeWithKey('e1', 'pred', 'a', { predicateId: 'p1' });
    graph.addDirectedEdgeWithKey('e2', 'a', 'b', { predicateId: 'p1' });

    const result = computeClusters(graph, new Set(['p1']));
    expect(result.nodeCluster.get('pred')).toBe(-1);
    const cluster = result.clusters.find((c) => c.has('a') && c.has('b'));
    expect(cluster).toBeDefined();
  });

  it('handles node with no neighbors in adjacency (BFS edge case)', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('a', { thingType: 'default' });
    graph.addNode('b', { thingType: 'default' });
    graph.addNode('isolated', { thingType: 'default' });
    graph.addDirectedEdgeWithKey('e1', 'a', 'b', { predicateId: 'p1' });

    const result = computeClusters(graph, new Set(['p1']));
    expect(result.nodeCluster.get('isolated')).toBe(-1);
    expect(result.clusters.find((c) => c.has('a') && c.has('b'))).toBeDefined();
  });
});
