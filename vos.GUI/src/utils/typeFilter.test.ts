import { describe, it, expect } from 'vitest';
import {
  discoverTypes,
  buildInstanceTypeIndex,
  applyTypeFilter,
} from './typeFilter';
import type { VosThing, VosRelationship } from '../types/vos';

function thing(id: string, name: string): VosThing {
  return { Id: id, Name: name, Properties: {} };
}
function rel(id: string, s: string, p: string, t: string): VosRelationship {
  return { Id: id, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} };
}

// Standard fixture: two type Things (Wall, Door), three Wall instances, one
// Door instance, plus an "is" predicate Thing.
const isP = thing('p-is', 'is');
const wallType = thing('t-wall', 'Wall');
const doorType = thing('t-door', 'Door');
const w1 = thing('w1', 'Wall-1');
const w2 = thing('w2', 'Wall-2');
const w3 = thing('w3', 'Wall-3');
const d1 = thing('d1', 'Door-1');

const things = [isP, wallType, doorType, w1, w2, w3, d1];

const rels: VosRelationship[] = [
  rel('r1', 'w1', 'p-is', 't-wall'),
  rel('r2', 'w2', 'p-is', 't-wall'),
  rel('r3', 'w3', 'p-is', 't-wall'),
  rel('r4', 'd1', 'p-is', 't-door'),
];

describe('discoverTypes (Feature #5362)', () => {
  it('returns one entry per type Thing with accurate counts', () => {
    const result = discoverTypes(things, rels);
    expect(result).toEqual([
      { typeId: 't-wall', name: 'Wall', instanceCount: 3 },
      { typeId: 't-door', name: 'Door', instanceCount: 1 },
    ]);
  });

  it('sorts by descending count, then by name alphabetically', () => {
    const extraType = thing('t-z', 'Zonal');
    const extraInst = thing('z1', 'Zonal-1');
    const extraRel = rel('r5', 'z1', 'p-is', 't-z');
    const r = discoverTypes([...things, extraType, extraInst], [...rels, extraRel]);
    // Wall(3) first, then Door(1) and Zonal(1) tied — sorted alphabetically
    expect(r.map((x) => x.name)).toEqual(['Wall', 'Door', 'Zonal']);
  });

  it('deduplicates duplicate `is` relationships from the same subject', () => {
    // IFC pipelines can emit multiple `is` rels for the same instance
    // (e.g. Tier 2 type-info pathway). Each subject should count once.
    const dupe = rel('rdup', 'w1', 'p-is', 't-wall');
    const r = discoverTypes(things, [...rels, dupe]);
    expect(r.find((x) => x.typeId === 't-wall')!.instanceCount).toBe(3);
  });

  it('ignores non-is relationships', () => {
    const hasP = thing('p-has', 'has');
    const containsRel = rel('rh', 'w1', 'p-has', 'd1');
    const r = discoverTypes([...things, hasP], [...rels, containsRel]);
    expect(r).toHaveLength(2); // still just Wall + Door
  });

  it('is case-insensitive on the predicate name', () => {
    const isUpperPred = thing('p-IS', 'IS');
    const upperRel = rel('rU', 'w1', 'p-IS', 't-wall');
    const r = discoverTypes([...things, isUpperPred], [...rels, upperRel]);
    expect(r.find((x) => x.typeId === 't-wall')!.instanceCount).toBe(3);
  });

  it('skips dangling references (target id not in things)', () => {
    const danglingRel = rel('rd', 'w1', 'p-is', 't-missing');
    const r = discoverTypes(things, [...rels, danglingRel]);
    expect(r.find((x) => x.typeId === 't-missing')).toBeUndefined();
  });

  it('returns [] for empty input', () => {
    expect(discoverTypes([], [])).toEqual([]);
    expect(discoverTypes(things, [])).toEqual([]);
  });
});

describe('buildInstanceTypeIndex (Feature #5362)', () => {
  it('maps each instance to its first-seen type', () => {
    const idx = buildInstanceTypeIndex(things, rels);
    expect(idx.get('w1')).toBe('t-wall');
    expect(idx.get('w2')).toBe('t-wall');
    expect(idx.get('d1')).toBe('t-door');
  });

  it('first-seen wins on multiple is relationships', () => {
    // Same instance with two type assignments — first wins.
    const second = rel('r2nd', 'w1', 'p-is', 't-door');
    const idx = buildInstanceTypeIndex(things, [...rels, second]);
    expect(idx.get('w1')).toBe('t-wall');
  });
});

describe('applyTypeFilter (Feature #5362)', () => {
  it('returns inputs unchanged when hiddenTypeIds is empty', () => {
    const result = applyTypeFilter(things, rels, new Set());
    expect(result.things).toBe(things);
    expect(result.relationships).toBe(rels);
  });

  it('removes instances of hidden types', () => {
    const result = applyTypeFilter(things, rels, new Set(['t-wall']));
    const ids = result.things.map((t) => t.Id);
    expect(ids).toContain('t-wall');   // type Thing itself stays
    expect(ids).toContain('t-door');
    expect(ids).toContain('d1');
    expect(ids).not.toContain('w1');
    expect(ids).not.toContain('w2');
    expect(ids).not.toContain('w3');
  });

  it('drops relationships whose endpoints were filtered out', () => {
    const haveContainment = thing('cT', 'Container');
    const cInst = thing('c1', 'C-1');
    const isCT = rel('rC', 'c1', 'p-is', 'cT');
    // Container c1 contains wall w1 (gets dropped because w1 is hidden)
    const hasP = thing('p-has', 'has');
    const contains = rel('rContains', 'c1', 'p-has', 'w1');
    const allThings = [...things, haveContainment, cInst, hasP];
    const allRels = [...rels, isCT, contains];

    const result = applyTypeFilter(allThings, allRels, new Set(['t-wall']));
    const relIds = result.relationships.map((r) => r.Id);
    expect(relIds).not.toContain('rContains');  // endpoint hidden
    expect(relIds).toContain('rC');             // both endpoints visible
  });

  it('keeps the type Thing visible even when its instances are hidden', () => {
    // Hide all walls but the Wall type Thing should remain in the graph as an
    // orientation hub.
    const result = applyTypeFilter(things, rels, new Set(['t-wall']));
    expect(result.things.find((t) => t.Id === 't-wall')).toBeDefined();
  });

  it('hides multiple types at once', () => {
    const result = applyTypeFilter(things, rels, new Set(['t-wall', 't-door']));
    const ids = result.things.map((t) => t.Id);
    expect(ids).not.toContain('w1');
    expect(ids).not.toContain('w2');
    expect(ids).not.toContain('d1');
    // Type Things still present
    expect(ids).toContain('t-wall');
    expect(ids).toContain('t-door');
  });
});
