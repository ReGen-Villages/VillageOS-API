import { describe, it, expect } from 'vitest';
import { buildGraph, getThingType, getLogicalChildren, countLogicalChildren, buildRelationshipIndex } from './graphologyMapper';
import type { VosThing, VosRelationship } from '../types/vos';

// ── Helpers ────────────────────────────────────────────────────────────

function makeThing(id: string, name: string, props: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: name, Properties: props };
}

function makeRel(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: `${subjectId}-${predicateId}-${targetId}`, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

// ── Test data ──────────────────────────────────────────────────────────

// Predicates
const isPred = makeThing('p-is', 'is');
const hasPred = makeThing('p-has', 'has');
const monitorsPred = makeThing('p-monitors', 'monitors');

// Types
const zone = makeThing('t-zone', 'Zone', { kind: 'type' });
const sensor = makeThing('t-sensor', 'Sensor', { kind: 'type' });

// Instances
const pickZone = makeThing('i-pickzone', 'PickZone', { capacity: '5000' });
const bulkZone = makeThing('i-bulkzone', 'BulkStorageZone', { capacity: '15000' });
const tempSensor = makeThing('i-temp', 'BulkTempSensor', { unit: 'celsius' });
const motionSensor = makeThing('i-motion', 'PickZoneMotionSensor', { unit: 'boolean' });
const weightSensor = makeThing('i-weight', 'InboundWeightSensor', { unit: 'kg' });
const leafNode = makeThing('i-leaf', 'LeafNode', {});

const allThings = [isPred, hasPred, monitorsPred, zone, sensor, pickZone, bulkZone, tempSensor, motionSensor, weightSensor, leafNode];

// Relationships
const rels: VosRelationship[] = [
  // Type classification
  makeRel('r1', 'i-pickzone', 'p-is', 't-zone'),      // PickZone is Zone
  makeRel('r2', 'i-bulkzone', 'p-is', 't-zone'),       // BulkStorageZone is Zone
  makeRel('r3', 'i-temp', 'p-is', 't-sensor'),          // BulkTempSensor is Sensor
  makeRel('r4', 'i-motion', 'p-is', 't-sensor'),        // PickZoneMotionSensor is Sensor
  makeRel('r5', 'i-weight', 'p-is', 't-sensor'),        // InboundWeightSensor is Sensor

  // Containment
  makeRel('r6', 'i-bulkzone', 'p-has', 'i-temp'),       // BulkStorageZone has BulkTempSensor
  makeRel('r7', 'i-pickzone', 'p-has', 'i-motion'),     // PickZone has PickZoneMotionSensor

  // Monitoring
  makeRel('r8', 'i-temp', 'p-monitors', 'i-bulkzone'),  // BulkTempSensor monitors BulkStorageZone
  makeRel('r9', 'i-motion', 'p-monitors', 'i-pickzone'), // MotionSensor monitors PickZone
];

// ── Tests ──────────────────────────────────────────────────────────────

describe('getThingType', () => {
  const thingMap = new Map(allThings.map((t) => [t.Id, t]));
  const index = buildRelationshipIndex(rels, thingMap);

  it('classifies predicate things used in relationships', () => {
    expect(getThingType(isPred, index)).toBe('predicate');
    expect(getThingType(hasPred, index)).toBe('predicate');
    expect(getThingType(monitorsPred, index)).toBe('predicate');
  });

  it('classifies things with ExecutablePath as predicates', () => {
    const handler = makeThing('h1', 'MyHandler', { ExecutablePath: '/bin/foo' });
    expect(getThingType(handler, index)).toBe('predicate');
  });

  it('classifies type targets of "is" relationships', () => {
    expect(getThingType(zone, index)).toBe('type');
    expect(getThingType(sensor, index)).toBe('type');
  });

  it('classifies regular instances as default', () => {
    expect(getThingType(pickZone, index)).toBe('default');
    expect(getThingType(tempSensor, index)).toBe('default');
    expect(getThingType(leafNode, index)).toBe('default');
  });
});

describe('buildGraph', () => {
  describe('node creation', () => {
    it('creates a node for every thing', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.order).toBe(allThings.length);
    });

    it('assigns labels from thing names', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getNodeAttribute('i-pickzone', 'label')).toBe('PickZone');
      expect(graph.getNodeAttribute('t-zone', 'label')).toBe('Zone');
    });

    it('classifies node types correctly', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getNodeAttribute('p-is', 'thingType')).toBe('predicate');
      expect(graph.getNodeAttribute('t-zone', 'thingType')).toBe('type');
      expect(graph.getNodeAttribute('i-pickzone', 'thingType')).toBe('default');
    });
  });

  describe('edge creation', () => {
    it('creates edges for all valid relationships', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.size).toBe(rels.length);
    });

    it('skips edges with missing endpoints', () => {
      const partialThings = [isPred, pickZone, zone]; // missing sensor, temp, etc.
      const graph = buildGraph(partialThings, rels);
      // Only r1 (pickzone is zone) has both endpoints present
      expect(graph.size).toBe(1);
    });

    it('assigns predicate ID as edge attribute', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getEdgeAttribute('r1', 'predicateId')).toBe('p-is');
      expect(graph.getEdgeAttribute('r6', 'predicateId')).toBe('p-has');
    });

    it('assigns predicate name as edge label', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getEdgeAttribute('r1', 'label')).toBe('is');
      expect(graph.getEdgeAttribute('r6', 'label')).toBe('has');
      expect(graph.getEdgeAttribute('r8', 'label')).toBe('monitors');
    });
  });

  describe('node sizing (incoming relationships only)', () => {
    it('gives minimum size (3) to nodes with no incoming edges', () => {
      const graph = buildGraph(allThings, rels);
      // LeafNode has no relationships at all
      expect(graph.getNodeAttribute('i-leaf', 'size')).toBe(3);
    });

    it('gives minimum size to nodes that only have outgoing edges', () => {
      // Create a source-only node: A -> B, A has outgoing but no incoming
      const a = makeThing('a', 'Source');
      const b = makeThing('b', 'Target');
      const pred = makeThing('pred', 'connects');
      const r = makeRel('r-ab', 'a', 'pred', 'b');
      const graph = buildGraph([a, b, pred], [r]);

      // Source (a) has 0 incoming → size = 3
      expect(graph.getNodeAttribute('a', 'size')).toBe(3);
      // Target (b) has 1 incoming → size = 3 + 1*1.5 = 4.5
      expect(graph.getNodeAttribute('b', 'size')).toBe(4.5);
    });

    it('scales size by incoming relationship count', () => {
      const graph = buildGraph(allThings, rels);

      // Zone is target of: r1 (PickZone is Zone), r2 (BulkStorageZone is Zone) = 2 incoming
      // size = 3 + 2 * 1.5 = 6
      expect(graph.getNodeAttribute('t-zone', 'size')).toBe(6);

      // Sensor is target of: r3, r4, r5 = 3 incoming
      // size = 3 + 3 * 1.5 = 7.5
      expect(graph.getNodeAttribute('t-sensor', 'size')).toBe(7.5);
    });

    it('does not count outgoing edges toward size', () => {
      const graph = buildGraph(allThings, rels);

      // BulkStorageZone: outgoing = r2 (is Zone), r6 (has temp); incoming = r8 (temp monitors it) = 1
      // size = 3 + 1 * 1.5 = 4.5
      expect(graph.getNodeAttribute('i-bulkzone', 'size')).toBe(4.5);
    });

    it('caps size at 15', () => {
      // Create a node that is the target of many relationships
      const hub = makeThing('hub', 'Hub');
      const pred = makeThing('pred', 'connects');
      const sources: VosThing[] = [];
      const hubRels: VosRelationship[] = [];
      for (let i = 0; i < 20; i++) {
        const src = makeThing(`s${i}`, `Source-${i}`);
        sources.push(src);
        hubRels.push(makeRel(`r-${i}`, `s${i}`, 'pred', 'hub'));
      }
      const graph = buildGraph([hub, pred, ...sources], hubRels);

      // 20 incoming → uncapped = 3 + 20*1.5 = 33, capped at 15
      expect(graph.getNodeAttribute('hub', 'size')).toBe(15);
    });
  });

  describe('geometry detection', () => {
    it('sets hasGeometry=true for things with latitude/longitude properties', () => {
      const geoThing = makeThing('g1', 'Home-1', { latitude: 40.7937, longitude: -73.6612 });
      const pred = makeThing('pred', 'is');
      const graph = buildGraph([geoThing, pred], []);
      expect(graph.getNodeAttribute('g1', 'hasGeometry')).toBe(true);
    });

    it('sets lat and lng attributes from latitude/longitude properties', () => {
      const geoThing = makeThing('g1', 'Home-1', { latitude: 40.7937, longitude: -73.6612 });
      const graph = buildGraph([geoThing], []);
      const lat = graph.getNodeAttribute('g1', 'lat') as number;
      const lng = graph.getNodeAttribute('g1', 'lng') as number;
      expect(lat).toBeCloseTo(40.7937, 3);
      expect(lng).toBeCloseTo(-73.6612, 3);
    });

    it('sets hasGeometry=false for things without lat/lng properties', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getNodeAttribute('i-pickzone', 'hasGeometry')).toBe(false);
      expect(graph.getNodeAttribute('p-is', 'hasGeometry')).toBe(false);
    });

    it('sets hasGeometry=false for things with non-numeric latitude', () => {
      const badGeo = makeThing('bad', 'BadGeo', { latitude: 'nope', longitude: -73.0 });
      const graph = buildGraph([badGeo], []);
      expect(graph.getNodeAttribute('bad', 'hasGeometry')).toBe(false);
    });

    it('does not set lat/lng on non-geo nodes', () => {
      const graph = buildGraph(allThings, rels);
      expect(graph.getNodeAttribute('i-pickzone', 'lat')).toBeUndefined();
      expect(graph.getNodeAttribute('i-pickzone', 'lng')).toBeUndefined();
    });
  });

  describe('node colours derived from relationships', () => {
    it('gives predicates amber colour', () => {
      const graph = buildGraph(allThings, rels);
      // 'is', 'has', 'monitors' are all used as PredicateId → predicate role
      expect(graph.getNodeAttribute('p-is', 'color')).toBe('#fbbf24');
      expect(graph.getNodeAttribute('p-has', 'color')).toBe('#fbbf24');
      expect(graph.getNodeAttribute('p-monitors', 'color')).toBe('#fbbf24');
    });

    it('gives type definitions blue colour', () => {
      const graph = buildGraph(allThings, rels);
      // Zone and Sensor are targets of "is" → type role
      expect(graph.getNodeAttribute('t-zone', 'color')).toBe('#60a5fa');
      expect(graph.getNodeAttribute('t-sensor', 'color')).toBe('#60a5fa');
    });

    it('derives instance colour from "is" type name consistently', () => {
      const graph = buildGraph(allThings, rels);
      // PickZone and BulkStorageZone are both "is Zone" → same colour
      const pickColor = graph.getNodeAttribute('i-pickzone', 'color');
      const bulkColor = graph.getNodeAttribute('i-bulkzone', 'color');
      expect(pickColor).toBe(bulkColor);
      // All three sensors are "is Sensor" → same colour
      const tempColor = graph.getNodeAttribute('i-temp', 'color');
      const motionColor = graph.getNodeAttribute('i-motion', 'color');
      const weightColor = graph.getNodeAttribute('i-weight', 'color');
      expect(tempColor).toBe(motionColor);
      expect(motionColor).toBe(weightColor);
    });

    it('gives different types different colours', () => {
      const graph = buildGraph(allThings, rels);
      const zoneInstanceColor = graph.getNodeAttribute('i-pickzone', 'color');
      const sensorInstanceColor = graph.getNodeAttribute('i-temp', 'color');
      // "Zone" and "Sensor" hash to different palette entries
      expect(zoneInstanceColor).not.toBe(sensorInstanceColor);
    });

    it('gives slate fallback to instances with no "is" relationship', () => {
      const graph = buildGraph(allThings, rels);
      // LeafNode has no "is" relationship → slate fallback
      expect(graph.getNodeAttribute('i-leaf', 'color')).toBe('#94a3b8');
    });

    it('uses vibrant (non-slate) colour for typed instances', () => {
      const graph = buildGraph(allThings, rels);
      const pickColor = graph.getNodeAttribute('i-pickzone', 'color');
      // Should NOT be the slate fallback
      expect(pickColor).not.toBe('#94a3b8');
    });
  });

  describe('multi-directed graph', () => {
    it('supports multiple edges between the same pair of nodes', () => {
      // BulkStorageZone has BulkTempSensor AND BulkTempSensor monitors BulkStorageZone
      const graph = buildGraph(allThings, rels);
      // outEdges: only edges from source → target in the given direction
      const outFromBulk = graph.outEdges('i-bulkzone', 'i-temp');
      const outFromTemp = graph.outEdges('i-temp', 'i-bulkzone');
      // r6: bulkzone -has-> temp
      expect(outFromBulk.length).toBe(1);
      expect(graph.getEdgeAttribute(outFromBulk[0], 'label')).toBe('has');
      // r8: temp -monitors-> bulkzone
      expect(outFromTemp.length).toBe(1);
      expect(graph.getEdgeAttribute(outFromTemp[0], 'label')).toBe('monitors');
    });
  });

  describe('logical node classification', () => {
    const geoProps = { latitude: 40.7937, longitude: -73.6612 };

    it('marks nodes without lat/lng as isLogical=true', () => {
      const graph = buildGraph(allThings, rels);
      // All test things lack lat/lng → all logical
      expect(graph.getNodeAttribute('p-is', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('t-zone', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('i-pickzone', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('i-leaf', 'isLogical')).toBe(true);
    });

    it('marks nodes with lat/lng as isLogical=false', () => {
      const geoNode = makeThing('geo-1', 'Home-1', geoProps);
      const graph = buildGraph([geoNode], []);
      expect(graph.getNodeAttribute('geo-1', 'isLogical')).toBe(false);
      expect(graph.getNodeAttribute('geo-1', 'hasGeometry')).toBe(true);
    });

    it('assigns parentGeoNodeId via "has" edge preferentially', () => {
      const geoParent = makeThing('geo-parent', 'Building-1', geoProps);
      const logicalChild = makeThing('logical-child', 'SensorType');
      const hasPredThing = makeThing('pred-has', 'has');
      const hasRel = makeRel('r-has', 'geo-parent', 'pred-has', 'logical-child');

      const graph = buildGraph([geoParent, logicalChild, hasPredThing], [hasRel]);
      expect(graph.getNodeAttribute('logical-child', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('logical-child', 'parentGeoNodeId')).toBe('geo-parent');
    });

    it('falls back to any geo neighbour when no "has" edge exists', () => {
      const geoNode = makeThing('geo-1', 'Building-1', geoProps);
      const logicalNode = makeThing('logical-1', 'SomeType');
      const predThing = makeThing('pred-monitors', 'monitors');
      const rel = makeRel('r-mon', 'logical-1', 'pred-monitors', 'geo-1');

      const graph = buildGraph([geoNode, logicalNode, predThing], [rel]);
      expect(graph.getNodeAttribute('logical-1', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('logical-1', 'parentGeoNodeId')).toBe('geo-1');
    });

    it('leaves parentGeoNodeId undefined for orphan logical nodes', () => {
      // LeafNode has no relationships → no geo neighbour
      const graph = buildGraph([leafNode], []);
      expect(graph.getNodeAttribute('i-leaf', 'isLogical')).toBe(true);
      expect(graph.getNodeAttribute('i-leaf', 'parentGeoNodeId')).toBeUndefined();
    });
  });

  describe('getLogicalChildren / countLogicalChildren', () => {
    const geoProps = { latitude: 40.7937, longitude: -73.6612 };

    it('returns logical children for a geo parent', () => {
      const geoParent = makeThing('geo-p', 'Building-1', geoProps);
      const childA = makeThing('child-a', 'TypeA');
      const childB = makeThing('child-b', 'TypeB');
      const hasPredThing = makeThing('pred-has', 'has');
      const relA = makeRel('r-a', 'geo-p', 'pred-has', 'child-a');
      const relB = makeRel('r-b', 'geo-p', 'pred-has', 'child-b');

      const graph = buildGraph([geoParent, childA, childB, hasPredThing], [relA, relB]);
      const children = getLogicalChildren(graph, 'geo-p');
      expect(children).toHaveLength(2);
      expect(children).toContain('child-a');
      expect(children).toContain('child-b');
      expect(countLogicalChildren(graph, 'geo-p')).toBe(2);
    });

    it('returns empty array for a geo node with no logical children', () => {
      const geoA = makeThing('geo-a', 'Building-A', geoProps);
      const geoB = makeThing('geo-b', 'Building-B', geoProps);
      const hasPredThing = makeThing('pred-has', 'has');
      // Edge between two geo nodes — no logical children
      const rel = makeRel('r-geo', 'geo-a', 'pred-has', 'geo-b');

      const graph = buildGraph([geoA, geoB, hasPredThing], [rel]);
      expect(getLogicalChildren(graph, 'geo-a')).toHaveLength(0);
      expect(countLogicalChildren(graph, 'geo-a')).toBe(0);
    });

    it('does not return children parented to a different geo node', () => {
      const geoA = makeThing('geo-a', 'Building-A', geoProps);
      const geoB = makeThing('geo-b', 'Building-B', geoProps);
      const childOfA = makeThing('child-of-a', 'SensorA');
      const childOfB = makeThing('child-of-b', 'SensorB');
      const hasPredThing = makeThing('pred-has', 'has');
      const relA = makeRel('r-a', 'geo-a', 'pred-has', 'child-of-a');
      const relB = makeRel('r-b', 'geo-b', 'pred-has', 'child-of-b');

      const graph = buildGraph([geoA, geoB, childOfA, childOfB, hasPredThing], [relA, relB]);
      const childrenA = getLogicalChildren(graph, 'geo-a');
      expect(childrenA).toHaveLength(1);
      expect(childrenA).toContain('child-of-a');

      const childrenB = getLogicalChildren(graph, 'geo-b');
      expect(childrenB).toHaveLength(1);
      expect(childrenB).toContain('child-of-b');
    });
  });

  describe('phased loading (incremental graph construction)', () => {
    // Phase 1: surface things — visible buildings with lat/lng
    const phase1Things: VosThing[] = [
      makeThing('b1', 'Building-1', { latitude: 40.7937, longitude: -73.6612 }),
    ];

    // Phase 2: remaining things — non-geo + non-surface geo
    const phase2Things: VosThing[] = [
      makeThing('p-is', 'is'),
      makeThing('p-has', 'has'),
      makeThing('t-zone', 'Zone'),
      makeThing('i-sensor', 'TempSensor', { unit: 'celsius' }),
      makeThing('room1', 'Room-1', { latitude: 40.7937, longitude: -73.6612 }),
    ];

    const allRels: VosRelationship[] = [
      makeRel('r1', 'i-sensor', 'p-is', 't-zone'),
      makeRel('r2', 'b1', 'p-has', 'i-sensor'),
      makeRel('r3', 'b1', 'p-has', 'room1'),
    ];

    it('phase 1: builds graph with surface things only', () => {
      const graph = buildGraph(phase1Things, allRels);
      expect(graph.order).toBe(1);
      expect(graph.hasNode('b1')).toBe(true);
      expect(graph.getNodeAttribute('b1', 'hasGeometry')).toBe(true);
    });

    it('phase 1: surface things have lat/lng', () => {
      const graph = buildGraph(phase1Things, allRels);
      expect(graph.getNodeAttribute('b1', 'lat')).toBeCloseTo(40.7937, 3);
      expect(graph.getNodeAttribute('b1', 'lng')).toBeCloseTo(-73.6612, 3);
    });

    it('phase 1: edges with missing endpoints are skipped', () => {
      const graph = buildGraph(phase1Things, allRels);
      // r2 and r3 reference b1 but sensor/room1 not loaded yet
      expect(graph.size).toBe(0);
    });

    it('phase 2: merging adds remaining nodes to graph', () => {
      const merged = [...phase1Things, ...phase2Things];
      const graph = buildGraph(merged, allRels);
      expect(graph.order).toBe(6); // 1 surface + 5 remaining
      expect(graph.hasNode('i-sensor')).toBe(true);
      expect(graph.hasNode('room1')).toBe(true);
    });

    it('phase 2: non-surface geo nodes have lat/lng from properties', () => {
      const merged = [...phase1Things, ...phase2Things];
      const graph = buildGraph(merged, allRels);
      expect(graph.getNodeAttribute('room1', 'hasGeometry')).toBe(true);
      expect(graph.getNodeAttribute('room1', 'lat')).toBeCloseTo(40.7937, 3);
    });

    it('phase 2: all edges now connect', () => {
      const merged = [...phase1Things, ...phase2Things];
      const graph = buildGraph(merged, allRels);
      expect(graph.size).toBe(3);
      expect(graph.hasEdge('r1')).toBe(true);
      expect(graph.hasEdge('r2')).toBe(true);
      expect(graph.hasEdge('r3')).toBe(true);
    });

    it('complete 2-phase produces same graph as single-pass buildGraph', () => {
      const allThings = [...phase1Things, ...phase2Things];
      const phasedGraph = buildGraph(allThings, allRels);

      // Single-pass: all things with full data
      const allThingsSinglePass: VosThing[] = [
        makeThing('b1', 'Building-1', { latitude: 40.7937, longitude: -73.6612 }),
        makeThing('p-is', 'is'),
        makeThing('p-has', 'has'),
        makeThing('t-zone', 'Zone'),
        makeThing('i-sensor', 'TempSensor', { unit: 'celsius' }),
        makeThing('room1', 'Room-1', { latitude: 40.7937, longitude: -73.6612 }),
      ];
      const singleGraph = buildGraph(allThingsSinglePass, allRels);

      expect(phasedGraph.order).toBe(singleGraph.order);
      expect(phasedGraph.size).toBe(singleGraph.size);

      expect(phasedGraph.getNodeAttribute('b1', 'hasGeometry'))
        .toBe(singleGraph.getNodeAttribute('b1', 'hasGeometry'));
      expect(phasedGraph.getNodeAttribute('room1', 'hasGeometry'))
        .toBe(singleGraph.getNodeAttribute('room1', 'hasGeometry'));
    });
  });
});
