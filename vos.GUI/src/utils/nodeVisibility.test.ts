import { describe, it, expect } from 'vitest';
import Graph from 'graphology';
import {
  buildLabelMatcher,
  getFullNeighborSet,
  edgeTouchesNode,
  isNonGeoVisibleOnMap,
  isGeoNode,
  computeOrbitPosition,
} from './nodeVisibility';
import type { SearchOptions } from './searchFilter';

// ── Helpers ────────────────────────────────────────────────────────────

function makeGraph(): Graph {
  const g = new Graph({ multi: true, type: 'directed' });

  // Geo nodes (physical)
  g.addNode('geo-a', { label: 'BuildingA', hasGeometry: true, lat: 40.79, lng: -73.66 });
  g.addNode('geo-b', { label: 'BuildingB', hasGeometry: true, lat: 40.80, lng: -73.65 });

  // Non-geo nodes (logical)
  g.addNode('logical-1', { label: 'SensorType', hasGeometry: false });
  g.addNode('logical-2', { label: 'Zone', hasGeometry: false });
  g.addNode('logical-3', { label: 'Controller', hasGeometry: false });

  // Predicate things
  g.addNode('pred-has', { label: 'has', hasGeometry: false, thingType: 'predicate' });
  g.addNode('pred-monitors', { label: 'monitors', hasGeometry: false, thingType: 'predicate' });

  // Edges
  g.addEdgeWithKey('e1', 'geo-a', 'logical-1', { predicateId: 'pred-has', label: 'has' });
  g.addEdgeWithKey('e2', 'geo-a', 'logical-2', { predicateId: 'pred-has', label: 'has' });
  g.addEdgeWithKey('e3', 'geo-b', 'logical-3', { predicateId: 'pred-has', label: 'has' });
  g.addEdgeWithKey('e4', 'logical-1', 'geo-b', { predicateId: 'pred-monitors', label: 'monitors' });
  g.addEdgeWithKey('e5', 'geo-a', 'geo-b', { predicateId: 'pred-monitors', label: 'monitors' });

  return g;
}

const defaultOpts: SearchOptions = { caseSensitive: false, exactMatch: false, useRegex: false };

// ── buildLabelMatcher ──────────────────────────────────────────────────

describe('buildLabelMatcher', () => {
  describe('single-term substring (default)', () => {
    it('matches by case-insensitive substring', () => {
      const matcher = buildLabelMatcher('sensor', defaultOpts);
      expect(matcher('SensorType')).toBe(true);
      expect(matcher('BulkTempSensor')).toBe(true);
      expect(matcher('Zone')).toBe(false);
    });

    it('is case-insensitive by default', () => {
      const matcher = buildLabelMatcher('BUILDING', defaultOpts);
      expect(matcher('BuildingA')).toBe(true);
    });
  });

  describe('case-sensitive', () => {
    it('respects case when caseSensitive=true', () => {
      const matcher = buildLabelMatcher('sensor', { caseSensitive: true, exactMatch: false, useRegex: false });
      expect(matcher('SensorType')).toBe(false);
      expect(matcher('sensorType')).toBe(true);
    });
  });

  describe('exact match', () => {
    it('matches only exact label (case-insensitive)', () => {
      const matcher = buildLabelMatcher('zone', { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(matcher('Zone')).toBe(true);
      expect(matcher('ZoneA')).toBe(false);
    });

    it('matches exact + case-sensitive', () => {
      const matcher = buildLabelMatcher('Zone', { caseSensitive: true, exactMatch: true, useRegex: false });
      expect(matcher('Zone')).toBe(true);
      expect(matcher('zone')).toBe(false);
    });
  });

  describe('comma-separated list', () => {
    it('matches any of the comma-separated terms', () => {
      const matcher = buildLabelMatcher('Sensor, Zone', defaultOpts);
      expect(matcher('SensorType')).toBe(true);
      expect(matcher('Zone')).toBe(true);
      expect(matcher('Controller')).toBe(false);
    });

    it('handles trailing commas', () => {
      const matcher = buildLabelMatcher('Sensor,', defaultOpts);
      expect(matcher('SensorType')).toBe(true);
      expect(matcher('Zone')).toBe(false);
    });

    it('returns false for all-empty terms', () => {
      const matcher = buildLabelMatcher(', , ', defaultOpts);
      expect(matcher('Anything')).toBe(false);
    });

    it('respects exactMatch with comma list', () => {
      const matcher = buildLabelMatcher('Zone, Controller', { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(matcher('Zone')).toBe(true);
      expect(matcher('ZoneA')).toBe(false);
      expect(matcher('controller')).toBe(true);
    });
  });

  describe('regex mode', () => {
    it('matches by regex pattern', () => {
      const matcher = buildLabelMatcher('^Build', { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(matcher('BuildingA')).toBe(true);
      expect(matcher('SensorType')).toBe(false);
    });

    it('respects caseSensitive in regex', () => {
      const matcher = buildLabelMatcher('^build', { caseSensitive: true, exactMatch: false, useRegex: true });
      expect(matcher('BuildingA')).toBe(false);
      expect(matcher('buildingA')).toBe(true);
    });

    it('falls back to plain text on invalid regex', () => {
      const matcher = buildLabelMatcher('[invalid', { caseSensitive: false, exactMatch: false, useRegex: true });
      // "[invalid" as plain text substring — no names should match
      expect(matcher('BuildingA')).toBe(false);
    });
  });
});

// ── getFullNeighborSet ──────────────────────────────────────────────────

describe('getFullNeighborSet', () => {
  it('returns all direct neighbors of a node', () => {
    const graph = makeGraph();
    const neighbors = getFullNeighborSet(graph, 'geo-a');
    // geo-a connects to: logical-1 (e1), logical-2 (e2), geo-b (e4 via logical-1, e5 direct)
    expect(neighbors.has('logical-1')).toBe(true);
    expect(neighbors.has('logical-2')).toBe(true);
    expect(neighbors.has('geo-b')).toBe(true);
  });

  it('returns empty set for non-existent node', () => {
    const graph = makeGraph();
    const neighbors = getFullNeighborSet(graph, 'nonexistent');
    expect(neighbors.size).toBe(0);
  });

  it('returns empty set for isolated node', () => {
    const graph = new Graph({ multi: true, type: 'directed' });
    graph.addNode('alone', { label: 'Alone', hasGeometry: false });
    const neighbors = getFullNeighborSet(graph, 'alone');
    expect(neighbors.size).toBe(0);
  });
});

// ── edgeTouchesNode ─────────────────────────────────────────────────────

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

// ── isNonGeoVisibleOnMap ───────────────────────────────────────────────

describe('isNonGeoVisibleOnMap', () => {
  it('returns false when no node is selected', () => {
    const neighbors = new Set(['logical-1', 'logical-2']);
    expect(isNonGeoVisibleOnMap('logical-1', null, neighbors)).toBe(false);
  });

  it('returns true when node is in selected neighbors set', () => {
    const neighbors = new Set(['logical-1', 'logical-2']);
    expect(isNonGeoVisibleOnMap('logical-1', 'geo-a', neighbors)).toBe(true);
  });

  it('returns false when node is not in selected neighbors set', () => {
    const neighbors = new Set(['logical-1']);
    expect(isNonGeoVisibleOnMap('logical-2', 'geo-a', neighbors)).toBe(false);
  });

  it('returns false for empty neighbor set even with selection', () => {
    const neighbors = new Set<string>();
    expect(isNonGeoVisibleOnMap('logical-1', 'geo-a', neighbors)).toBe(false);
  });
});

// ── isGeoNode ────────────────────────────────────────────────────────────

describe('isGeoNode', () => {
  it('returns true for node with hasGeometry + lat + lng', () => {
    expect(isGeoNode({ hasGeometry: true, lat: 40.79, lng: -73.66 })).toBe(true);
  });

  it('returns false when hasGeometry is false', () => {
    expect(isGeoNode({ hasGeometry: false, lat: 40.79, lng: -73.66 })).toBe(false);
  });

  it('returns false when hasGeometry is missing', () => {
    expect(isGeoNode({ lat: 40.79, lng: -73.66 })).toBe(false);
  });

  it('returns false when lat is missing', () => {
    expect(isGeoNode({ hasGeometry: true, lng: -73.66 })).toBe(false);
  });

  it('returns false when lng is missing', () => {
    expect(isGeoNode({ hasGeometry: true, lat: 40.79 })).toBe(false);
  });

  it('returns false when lat is not a number', () => {
    expect(isGeoNode({ hasGeometry: true, lat: 'north', lng: -73.66 })).toBe(false);
  });

  it('returns false for empty attributes', () => {
    expect(isGeoNode({})).toBe(false);
  });
});

// ── computeOrbitPosition ─────────────────────────────────────────────────

describe('computeOrbitPosition', () => {
  const CENTER_LAT = 40.0;
  const CENTER_LNG = -73.0;
  const RADIUS = 0.001;

  it('first node (index 0) is placed at 12 o\'clock (north)', () => {
    const pos = computeOrbitPosition(CENTER_LAT, CENTER_LNG, 0, 4, RADIUS);
    // angle = -π/2 → sin = -1, cos = 0 → lat decreases, lng unchanged
    // Wait: sin(-π/2) = -1, so lat = center - radius (south, not north)
    // Actually at angle -π/2: sin = -1, cos ≈ 0
    expect(pos.lat).toBeCloseTo(CENTER_LAT - RADIUS, 10);
    expect(pos.lng).toBeCloseTo(CENTER_LNG, 10);
  });

  it('positions are evenly spaced around the center', () => {
    const count = 4;
    const positions = Array.from({ length: count }, (_, i) =>
      computeOrbitPosition(CENTER_LAT, CENTER_LNG, i, count, RADIUS),
    );

    // All should be at the same distance from center
    for (const pos of positions) {
      const dLat = pos.lat - CENTER_LAT;
      const dLng = pos.lng - CENTER_LNG;
      const dist = Math.sqrt(dLat * dLat + dLng * dLng);
      expect(dist).toBeCloseTo(RADIUS, 10);
    }
  });

  it('single node orbit places it at the start angle', () => {
    const pos = computeOrbitPosition(CENTER_LAT, CENTER_LNG, 0, 1, RADIUS);
    expect(pos.lat).toBeCloseTo(CENTER_LAT - RADIUS, 10);
    expect(pos.lng).toBeCloseTo(CENTER_LNG, 10);
  });

  it('handles count of 0 without division by zero', () => {
    // count = 0 → Math.max(0, 1) = 1, so no crash
    const pos = computeOrbitPosition(CENTER_LAT, CENTER_LNG, 0, 0, RADIUS);
    expect(typeof pos.lat).toBe('number');
    expect(typeof pos.lng).toBe('number');
    expect(Number.isFinite(pos.lat)).toBe(true);
  });

  it('returns center when radius is 0', () => {
    const pos = computeOrbitPosition(CENTER_LAT, CENTER_LNG, 0, 4, 0);
    expect(pos.lat).toBe(CENTER_LAT);
    expect(pos.lng).toBe(CENTER_LNG);
  });
});

