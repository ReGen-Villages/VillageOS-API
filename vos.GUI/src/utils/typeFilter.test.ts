import { describe, it, expect } from 'vitest';
import {
  discoverTypes,
  groupTypesByName,
  buildInstanceTypeIndex,
  applyTypeFilter,
  NO_TYPE_ID,
  NO_TYPE_NAME,
  type TypeStat,
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
  // Standard fixture has 7 Things: isP, wallType, doorType, w1..w3, d1.
  // Bucketing — each Thing in exactly one bucket (its `is`-target or no-type):
  //   Wall (t-wall): w1, w2, w3 → 3
  //   Door (t-door): d1 → 1
  //   no-type: isP, wallType, doorType → 3 (none has its own `is` relation)
  it('returns one entry per type Thing PLUS the synthetic no-type bucket (no-type pinned last)', () => {
    const result = discoverTypes(things, rels);
    expect(result).toEqual([
      { typeId: 't-wall', name: 'Wall', instanceCount: 3 },
      { typeId: 't-door', name: 'Door', instanceCount: 1 },
      { typeId: NO_TYPE_ID, name: NO_TYPE_NAME, instanceCount: 3 },
    ]);
  });

  it('sum of all instanceCounts equals things.length (every Thing counted exactly once)', () => {
    const result = discoverTypes(things, rels);
    const sum = result.reduce((s, t) => s + t.instanceCount, 0);
    expect(sum).toBe(things.length);
  });

  it('real types sort by count desc then name asc; no-type bucket pinned last', () => {
    const extraType = thing('t-z', 'Zonal');
    const extraInst = thing('z1', 'Zonal-1');
    const extraRel = rel('r5', 'z1', 'p-is', 't-z');
    const r = discoverTypes([...things, extraType, extraInst], [...rels, extraRel]);
    // Real types: Wall(3), Door(1), Zonal(1) → Wall, Door, Zonal (Door < Zonal alphabetically)
    // no-type pinned last regardless of its count
    expect(r.map((x) => x.name)).toEqual(['Wall', 'Door', 'Zonal', NO_TYPE_NAME]);
  });

  it('deduplicates duplicate `is` relationships from the same subject', () => {
    const dupe = rel('rdup', 'w1', 'p-is', 't-wall');
    const r = discoverTypes(things, [...rels, dupe]);
    expect(r.find((x) => x.typeId === 't-wall')!.instanceCount).toBe(3);
  });

  it('ignores non-is relationships', () => {
    const hasP = thing('p-has', 'has');
    const containsRel = rel('rh', 'w1', 'p-has', 'd1');
    const r = discoverTypes([...things, hasP], [...rels, containsRel]);
    // Bucket totals: Wall(3), Door(1), no-type(4 — isP, wallType, doorType, hasP)
    expect(r.find((x) => x.typeId === NO_TYPE_ID)!.instanceCount).toBe(4);
  });

  it('is case-insensitive on the predicate name', () => {
    const isUpperPred = thing('p-IS', 'IS');
    const upperRel = rel('rU', 'w1', 'p-IS', 't-wall');
    const r = discoverTypes([...things, isUpperPred], [...rels, upperRel]);
    // w1 already classified by 'is' (lowercase) — first-seen wins — count unchanged.
    expect(r.find((x) => x.typeId === 't-wall')!.instanceCount).toBe(3);
  });

  it('treats Things with dangling `is` references as no-type (skips the missing target)', () => {
    const w4 = thing('w4', 'Wall-4');
    const danglingRel = rel('rd', 'w4', 'p-is', 't-missing');
    const r = discoverTypes([...things, w4], [...rels, danglingRel]);
    expect(r.find((x) => x.typeId === 't-missing')).toBeUndefined();
    // w4's dangling `is` collapses to no-type bucket — was 3 (isP, wallType, doorType) + w4 = 4
    expect(r.find((x) => x.typeId === NO_TYPE_ID)!.instanceCount).toBe(4);
  });

  it('returns [] for empty input', () => {
    expect(discoverTypes([], [])).toEqual([]);
  });

  it('returns one no-type entry when there are Things but no `is` relationships', () => {
    const r = discoverTypes(things, []);
    expect(r).toEqual([{ typeId: NO_TYPE_ID, name: NO_TYPE_NAME, instanceCount: things.length }]);
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

  it('hides instances AND the type Thing itself when its bucket is hidden', () => {
    const result = applyTypeFilter(things, rels, new Set(['t-wall']));
    const ids = result.things.map((t) => t.Id);
    expect(ids).not.toContain('w1');
    expect(ids).not.toContain('w2');
    expect(ids).not.toContain('w3');
    expect(ids).not.toContain('t-wall');  // type Thing also gone
    // Other buckets untouched
    expect(ids).toContain('t-door');
    expect(ids).toContain('d1');
    expect(ids).toContain('p-is');        // no-type bucket not hidden
  });

  it('hides only no-type Things when only NO_TYPE_ID is hidden', () => {
    const result = applyTypeFilter(things, rels, new Set([NO_TYPE_ID]));
    const ids = result.things.map((t) => t.Id);
    // No-type bucket = isP, wallType, doorType (all Things without an `is`)
    expect(ids).not.toContain('p-is');
    expect(ids).not.toContain('t-wall');
    expect(ids).not.toContain('t-door');
    // Classified instances survive — they have an `is` to a not-hidden type
    expect(ids).toContain('w1');
    expect(ids).toContain('d1');
  });

  it('drops relationships whose endpoints became hidden', () => {
    const hasP = thing('p-has', 'has');
    const contains = rel('rContains', 'w1', 'p-has', 'w2');
    const allRels = [...rels, contains];
    const result = applyTypeFilter([...things, hasP], allRels, new Set(['t-wall']));
    const relIds = result.relationships.map((r) => r.Id);
    expect(relIds).not.toContain('rContains');  // both walls hidden
    expect(relIds).not.toContain('r1');         // is-rel to hidden t-wall
    expect(relIds).toContain('r4');             // d1 → t-door, both still visible
  });

  it('hiding ALL discovered types AND no-type yields an empty graph', () => {
    // The "None" action in the panel computes this set: every typeId from
    // discoverTypes including NO_TYPE_ID. Result must be zero Things and zero
    // relationships — matches user expectation that "None" means "show
    // nothing".
    const allTypeIds = new Set(discoverTypes(things, rels).map((t) => t.typeId));
    const result = applyTypeFilter(things, rels, allTypeIds);
    expect(result.things).toEqual([]);
    expect(result.relationships).toEqual([]);
  });

  it('hides multiple types at once', () => {
    const result = applyTypeFilter(things, rels, new Set(['t-wall', 't-door']));
    const ids = result.things.map((t) => t.Id);
    expect(ids).not.toContain('w1');
    expect(ids).not.toContain('w2');
    expect(ids).not.toContain('d1');
    // Both type Things now also hidden (changed from prior behavior)
    expect(ids).not.toContain('t-wall');
    expect(ids).not.toContain('t-door');
    // No-type bucket survives
    expect(ids).toContain('p-is');
  });
});

describe('groupTypesByName (Bug #5363 — coalesce same-named types)', () => {
  it('returns the same shape when every Name is unique', () => {
    const stats: TypeStat[] = [
      { typeId: 't-wall', name: 'Wall', instanceCount: 3 },
      { typeId: 't-door', name: 'Door', instanceCount: 1 },
      { typeId: NO_TYPE_ID, name: NO_TYPE_NAME, instanceCount: 3 },
    ];
    const groups = groupTypesByName(stats);
    expect(groups).toEqual([
      { name: 'Wall', typeIds: ['t-wall'], instanceCount: 3 },
      { name: 'Door', typeIds: ['t-door'], instanceCount: 1 },
      { name: NO_TYPE_NAME, typeIds: [NO_TYPE_ID], instanceCount: 3 },
    ]);
  });

  it('coalesces same-named entries into one group with all typeIds + summed counts', () => {
    // The MV IFC seed reproducer: five distinct type-Things share a Name.
    const stats: TypeStat[] = [
      { typeId: 't-sp1', name: 'Solar_Panel-Tesla:Solar Panel', instanceCount: 1014 },
      { typeId: 't-sp2', name: 'Solar_Panel-Tesla:Solar Panel', instanceCount: 295 },
      { typeId: 't-sp3', name: 'Solar_Panel-Tesla:Solar Panel', instanceCount: 69 },
      { typeId: 't-sp4', name: 'Solar_Panel-Tesla:Solar Panel', instanceCount: 69 },
      { typeId: 't-sp5', name: 'Solar_Panel-Tesla:Solar Panel', instanceCount: 69 },
      { typeId: 't-door', name: 'Door', instanceCount: 12 },
    ];
    const groups = groupTypesByName(stats);
    expect(groups).toHaveLength(2);
    const sp = groups[0];
    expect(sp.name).toBe('Solar_Panel-Tesla:Solar Panel');
    expect(sp.typeIds).toEqual(['t-sp1', 't-sp2', 't-sp3', 't-sp4', 't-sp5']);
    expect(sp.instanceCount).toBe(1014 + 295 + 69 + 69 + 69);
    expect(groups[1]).toEqual({ name: 'Door', typeIds: ['t-door'], instanceCount: 12 });
  });

  it('preserves first-appearance order from the input', () => {
    // Input order is what discoverTypes returns (count desc, no-type last).
    // groupTypesByName should not re-sort; the first appearance of each Name
    // determines the group's position.
    const stats: TypeStat[] = [
      { typeId: 't-zonal', name: 'Zonal', instanceCount: 100 },
      { typeId: 't-wall1', name: 'Wall', instanceCount: 80 },
      { typeId: 't-door', name: 'Door', instanceCount: 50 },
      { typeId: 't-wall2', name: 'Wall', instanceCount: 5 },
    ];
    const groups = groupTypesByName(stats);
    expect(groups.map((g) => g.name)).toEqual(['Zonal', 'Wall', 'Door']);
    // Wall group inherits Wall's first appearance (between Zonal and Door)
    expect(groups[1].instanceCount).toBe(85);
    expect(groups[1].typeIds).toEqual(['t-wall1', 't-wall2']);
  });

  it('returns empty for empty input', () => {
    expect(groupTypesByName([])).toEqual([]);
  });

  it('sum invariant — total instanceCount unchanged after grouping', () => {
    const stats: TypeStat[] = [
      { typeId: 'a', name: 'X', instanceCount: 7 },
      { typeId: 'b', name: 'X', instanceCount: 3 },
      { typeId: 'c', name: 'Y', instanceCount: 11 },
    ];
    const before = stats.reduce((s, t) => s + t.instanceCount, 0);
    const after = groupTypesByName(stats).reduce((s, g) => s + g.instanceCount, 0);
    expect(after).toBe(before);
  });
});
