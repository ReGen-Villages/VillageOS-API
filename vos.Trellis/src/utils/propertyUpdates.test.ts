import { describe, it, expect } from 'vitest';
import {
  applyThingPropertyUpdate,
  applyRelationshipPropertyUpdate,
  isVisibleRelationship,
} from './propertyUpdates';
import type { VosThing, VosRelationship } from '../types/vos';

const makeThing = (overrides?: Partial<VosThing>): VosThing => ({
  Id: 'thing-1',
  Name: 'Test Thing',
  Properties: { water_level: 50, unit: 'litres' },
  ...overrides,
});

const makeRel = (overrides?: Partial<VosRelationship>): VosRelationship => ({
  Id: 'rel-1',
  Name: 'consumes',
  SubjectId: 'node-A',
  PredicateId: 'pred-1',
  TargetId: 'node-B',
  Properties: { quantity: 5, unit: 'kWh' },
  ...overrides,
});

describe('applyThingPropertyUpdate', () => {
  it('merges new property while preserving existing', () => {
    const thing = makeThing();
    const updated = applyThingPropertyUpdate(thing, 'temperature', 22);
    expect(updated.Properties.temperature).toBe(22);
    expect(updated.Properties.water_level).toBe(50);
    expect(updated.Properties.unit).toBe('litres');
  });

  it('overwrites existing property value', () => {
    const thing = makeThing();
    const updated = applyThingPropertyUpdate(thing, 'water_level', 30);
    expect(updated.Properties.water_level).toBe(30);
    expect(updated.Properties.unit).toBe('litres');
  });

  it('does not mutate the original thing', () => {
    const thing = makeThing();
    applyThingPropertyUpdate(thing, 'water_level', 30);
    expect(thing.Properties.water_level).toBe(50);
  });
});

describe('applyRelationshipPropertyUpdate', () => {
  it('merges new property while preserving existing', () => {
    const rel = makeRel();
    const updated = applyRelationshipPropertyUpdate(rel, 'total_consumed', 100);
    expect(updated.Properties.total_consumed).toBe(100);
    expect(updated.Properties.quantity).toBe(5);
    expect(updated.Properties.unit).toBe('kWh');
  });

  it('overwrites existing property value', () => {
    const rel = makeRel();
    const updated = applyRelationshipPropertyUpdate(rel, 'quantity', 10);
    expect(updated.Properties.quantity).toBe(10);
  });

  it('handles relationship with empty Properties', () => {
    const rel = makeRel({ Properties: {} });
    const updated = applyRelationshipPropertyUpdate(rel, 'quantity', 5);
    expect(updated.Properties.quantity).toBe(5);
  });

  it('does not mutate the original relationship', () => {
    const rel = makeRel();
    applyRelationshipPropertyUpdate(rel, 'quantity', 999);
    expect(rel.Properties.quantity).toBe(5);
  });
});

describe('isVisibleRelationship', () => {
  const rels = [
    makeRel({ Id: 'rel-1', SubjectId: 'node-A', TargetId: 'node-B' }),
    makeRel({ Id: 'rel-2', SubjectId: 'node-B', TargetId: 'node-C' }),
    makeRel({ Id: 'rel-3', SubjectId: 'node-C', TargetId: 'node-A' }),
  ];

  it('returns true when rel subject matches selected node', () => {
    expect(isVisibleRelationship('rel-1', 'node-A', rels)).toBe(true);
  });

  it('returns true when rel target matches selected node', () => {
    expect(isVisibleRelationship('rel-3', 'node-A', rels)).toBe(true);
  });

  it('returns false when rel is not connected to selected node', () => {
    expect(isVisibleRelationship('rel-2', 'node-A', rels)).toBe(false);
  });

  it('returns false when no node is selected', () => {
    expect(isVisibleRelationship('rel-1', null, rels)).toBe(false);
  });

  it('returns false when relId does not exist', () => {
    expect(isVisibleRelationship('nonexistent', 'node-A', rels)).toBe(false);
  });
});
