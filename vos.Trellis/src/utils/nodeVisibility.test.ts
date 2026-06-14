import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import { buildLabelMatcher, decideEdgeDisplay, edgeTouchesNode, isContainmentPredicate } from './nodeVisibility';
import type { SearchOptions } from './searchFilter';

function makeGraph(): Graph {
  const g = new Graph({ multi: true, type: 'directed' });

  g.addNode('geo-a', { label: 'BuildingA', hasGeometry: true });
  g.addNode('geo-b', { label: 'BuildingB', hasGeometry: true });

  g.addNode('logical-1', { label: 'SensorType', hasGeometry: false });
  g.addNode('logical-2', { label: 'Zone', hasGeometry: false });

  g.addEdgeWithKey('e1', 'geo-a', 'logical-1', { predicateId: 'pred-has', label: 'has' });
  g.addEdgeWithKey('e2', 'geo-a', 'logical-2', { predicateId: 'pred-has', label: 'has' });
  g.addEdgeWithKey('e5', 'geo-a', 'geo-b', { predicateId: 'pred-monitors', label: 'monitors' });

  return g;
}

const defaultOpts: SearchOptions = { caseSensitive: false, exactMatch: false, useRegex: false };

describe('buildLabelMatcher', () => {
  it('matches by case-insensitive substring', () => {
    const matcher = buildLabelMatcher('sensor', defaultOpts);
    expect(matcher('SensorType')).toBe(true);
    expect(matcher('Zone')).toBe(false);
  });

  it('respects case when caseSensitive=true', () => {
    const matcher = buildLabelMatcher('sensor', { caseSensitive: true, exactMatch: false, useRegex: false });
    expect(matcher('SensorType')).toBe(false);
    expect(matcher('sensorType')).toBe(true);
  });

  it('matches exact label when exactMatch=true', () => {
    const matcher = buildLabelMatcher('zone', { caseSensitive: false, exactMatch: true, useRegex: false });
    expect(matcher('Zone')).toBe(true);
    expect(matcher('ZoneA')).toBe(false);
  });

  it('matches comma-separated terms', () => {
    const matcher = buildLabelMatcher('Sensor, Zone', defaultOpts);
    expect(matcher('SensorType')).toBe(true);
    expect(matcher('Zone')).toBe(true);
    expect(matcher('Controller')).toBe(false);
  });

  it('matches by regex pattern', () => {
    const matcher = buildLabelMatcher('^Build', { caseSensitive: false, exactMatch: false, useRegex: true });
    expect(matcher('BuildingA')).toBe(true);
    expect(matcher('SensorType')).toBe(false);
  });
});

describe('edgeTouchesNode', () => {
  it('returns true when edge source matches nodeId', () => {
    const graph = makeGraph();
    expect(edgeTouchesNode(graph, 'e1', 'geo-a')).toBe(true);
  });

  it('returns true when edge target matches nodeId', () => {
    const graph = makeGraph();
    expect(edgeTouchesNode(graph, 'e1', 'logical-1')).toBe(true);
  });

  it('returns false when edge does not touch nodeId', () => {
    const graph = makeGraph();
    expect(edgeTouchesNode(graph, 'e1', 'geo-b')).toBe(false);
  });

  it('returns false when nodeId is null', () => {
    const graph = makeGraph();
    expect(edgeTouchesNode(graph, 'e1', null)).toBe(false);
  });
});

describe('decideEdgeDisplay (Feature #5344, simplified by Bug #5364)', () => {
  const NEUTRAL = {
    endpointMatchesHover: false,
    endpointMatchesSelection: false,
    bothEndpointsInSearch: undefined,
    predicateInActiveFilter: undefined,
  } as const;

  it('shows edges by default when no filter is active', () => {
    expect(decideEdgeDisplay(NEUTRAL)).toBe('show');
  });

  it('brightens edges touching the hovered node', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, endpointMatchesHover: true })).toBe('brighten');
  });

  it('shows edges touching the selected node', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, endpointMatchesSelection: true })).toBe('show');
  });

  it('hides non-matching edges when search is active', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, bothEndpointsInSearch: false })).toBe('hide');
  });

  it('shows matching edges when search is active', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, bothEndpointsInSearch: true })).toBe('show');
  });

  it('hides non-matching edges when predicate filter is active', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, predicateInActiveFilter: false })).toBe('hide');
  });

  it('shows matching edges when predicate filter is active', () => {
    expect(decideEdgeDisplay({ ...NEUTRAL, predicateInActiveFilter: true })).toBe('show');
  });

  it('precedence: hover > selection > search > predicate > default', () => {
    // All conditions true, hover wins
    expect(decideEdgeDisplay({
      endpointMatchesHover: true,
      endpointMatchesSelection: true,
      bothEndpointsInSearch: true,
      predicateInActiveFilter: true,
    })).toBe('brighten');

    // Search beats predicate
    expect(decideEdgeDisplay({
      ...NEUTRAL, bothEndpointsInSearch: false, predicateInActiveFilter: true,
    })).toBe('hide');
  });
});

describe('isContainmentPredicate', () => {
  it('returns true when __IsMapContainmentPredicate is true', () => {
    expect(isContainmentPredicate({ __IsMapContainmentPredicate: true })).toBe(true);
  });

  it('returns false when flag is missing', () => {
    expect(isContainmentPredicate({})).toBe(false);
  });

  it('returns false when props is undefined', () => {
    expect(isContainmentPredicate(undefined)).toBe(false);
  });
});
