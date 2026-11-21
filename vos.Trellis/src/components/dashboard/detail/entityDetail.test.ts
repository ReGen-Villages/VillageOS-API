import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '@tendril-ui/tendril';
import type { VosThing, VosRelationship, StateTransition } from '@tendril-ui/tendril';
import { resolveRelations, buildStateChanges } from './entityDetail';

function thing(id: string, name: string, properties: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: name, Properties: properties };
}
function relationship(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: id, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

// site -has-> building -has-> array, site -has-> spring, site -references-> catchment -contains-> site.
// `has` reaches both a building and a spring, so archetype filtering must tell them apart.
function fixture() {
  const things = [
    thing('site', 'SITE-1'),
    thing('building', 'SITE-1-B1', { quantity: 5, code: 'RES-9' }),
    thing('array', 'SITE-1-B1-A', { quantity: 5 }),
    thing('reservoir', 'RES-9', { code: 'RES-9', description: 'Upper reservoir' }),
    thing('spring', 'SPR-1', { display_name: 'North spring' }),
    thing('catchment', 'CAT-1', { catchment_number: 'C-1' }),
    thing('Building', 'Building'),
    thing('Array', 'Array'),
    thing('Reservoir', 'Reservoir'),
    thing('Spring', 'Spring'),
    thing('Catchment', 'Catchment'),
    thing('has', 'has'),
    thing('references', 'references'),
    thing('contains', 'contains'),
    thing('is', 'is'),
  ];
  const relationships = [
    relationship('r1', 'site', 'has', 'building'),
    relationship('r2', 'building', 'has', 'array'),
    relationship('r3', 'site', 'has', 'spring'),
    relationship('r4', 'site', 'references', 'catchment'),
    relationship('r5', 'catchment', 'contains', 'site'),
    relationship('r6', 'building', 'references', 'reservoir'),
    relationship('i1', 'building', 'is', 'Building'),
    relationship('i2', 'array', 'is', 'Array'),
    relationship('i3', 'spring', 'is', 'Spring'),
    relationship('i4', 'catchment', 'is', 'Catchment'),
    relationship('i5', 'reservoir', 'is', 'Reservoir'),
  ];
  return buildModelIndex(things, relationships);
}

describe('resolveRelations', () => {
  it('follows an outbound predicate and keeps only the configured archetype', () => {
    const index = fixture();
    const [group] = resolveRelations('site', index, [
      { predicate: 'has', direction: 'out', archetype: 'Building', label: 'Buildings' },
    ]);
    expect(group.label).toBe('Buildings');
    expect(group.edges.map((e) => e.relatedName)).toEqual(['SITE-1-B1']);
    expect(group.edges.some((e) => e.relatedName === 'SPR-1')).toBe(false);
  });

  it('names the subject and target of each edge by direction', () => {
    const index = fixture();
    const [out] = resolveRelations('site', index, [{ predicate: 'has', direction: 'out', archetype: 'Building' }]);
    expect(out.edges[0]).toMatchObject({ subjectName: 'SITE-1', targetName: 'SITE-1-B1' });
    const [inbound] = resolveRelations('site', index, [{ predicate: 'contains', direction: 'in', archetype: 'Catchment' }]);
    expect(inbound.edges[0]).toMatchObject({ subjectName: 'CAT-1', targetName: 'SITE-1' });
  });

  it('shows only the requested properties, or all with "*"', () => {
    const index = fixture();
    const [some] = resolveRelations('site', index, [
      { predicate: 'has', archetype: 'Building', properties: ['quantity'] },
    ]);
    expect(some.edges[0].properties).toEqual([['quantity', 5]]);
    const [all] = resolveRelations('site', index, [
      { predicate: 'has', archetype: 'Building', properties: '*' },
    ]);
    expect(all.edges[0].properties).toEqual(expect.arrayContaining([['quantity', 5], ['code', 'RES-9']]));
    const [none] = resolveRelations('site', index, [{ predicate: 'has', archetype: 'Building' }]);
    expect(none.edges[0].properties).toEqual([]);
  });

  it('nests child relations from each matched Thing', () => {
    const index = fixture();
    const [group] = resolveRelations('site', index, [
      {
        predicate: 'has',
        archetype: 'Building',
        relations: [{ predicate: 'has', archetype: 'Array', label: 'Array', properties: ['quantity'] }],
      },
    ]);
    const child = group.edges[0].children[0];
    expect(child.label).toBe('Array');
    expect(child.edges[0].relatedName).toBe('SITE-1-B1-A');
    expect(child.edges[0].properties).toEqual([['quantity', 5]]);
  });

  it('folds an inline relation onto the parent row instead of nesting it', () => {
    const index = fixture();
    const [group] = resolveRelations('site', index, [
      {
        predicate: 'has',
        archetype: 'Building',
        properties: ['quantity'],
        relations: [{ predicate: 'references', archetype: 'Reservoir', properties: ['code'], inline: true }],
      },
    ]);
    const building = group.edges[0];
    expect(building.properties).toEqual([['code', 'RES-9'], ['quantity', 5]]);
    expect(building.children).toEqual([]);
  });

  it('preserves the configured order of relation groups', () => {
    const index = fixture();
    const groups = resolveRelations('site', index, [
      { predicate: 'references', archetype: 'Catchment', label: 'Catchment' },
      { predicate: 'has', archetype: 'Spring', label: 'Spring' },
    ]);
    expect(groups.map((g) => g.label)).toEqual(['Catchment', 'Spring']);
  });

  it('is cycle-guarded when a relation loops back to the root', () => {
    const index = fixture();
    // site -references-> catchment -contains-> site: the nested contains must not re-expand the root.
    const [group] = resolveRelations('site', index, [
      { predicate: 'references', archetype: 'Catchment', relations: [{ predicate: 'contains', direction: 'out' }] },
    ]);
    const catchmentChildren = group.edges[0].children[0];
    expect(catchmentChildren.edges.some((e) => e.thingId === 'site')).toBe(false);
  });
});

describe('buildStateChanges', () => {
  const transition = (at: string, entered: string[], exited: string[]): StateTransition => ({
    At: at,
    Entered: entered,
    Exited: exited,
    States: entered,
  });

  it('orders changes oldest first', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:31:00Z', ['overflowing'], []),
      transition('2026-07-14T09:14:00Z', ['filling'], []),
    ]);
    expect(changes.map((c) => c.at)).toEqual(['2026-07-14T09:14:00Z', '2026-07-14T09:31:00Z']);
    expect(changes[0].entered).toEqual(['filling']);
  });

  it('drops transitions that neither enter nor exit a state', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:14:00Z', ['filling'], []),
      transition('2026-07-14T09:20:00Z', [], []),
    ]);
    expect(changes).toHaveLength(1);
  });
});
