import { describe, it, expect } from 'vitest';
import type { Composition } from '../types/dashboard';
import type { VosThing, VosRelationship } from '../types/vos';
import { buildModelIndex } from '../api/dashboardApi';
import { columnKey, hopsOf, pageOf, tableOf, unresolvedNames } from './composer';

const SPRINGS: Composition = {
  kind: 'Spring',
  columns: [
    { source: 'property', name: 'flow', numeric: true },
    { source: 'path', steps: [{ predicate: 'feeds', direction: 'out', archetype: 'Reservoir' }], label: 'feeds → Reservoir' },
    { source: 'path', steps: [{ predicate: 'feeds', direction: 'out', archetype: 'Reservoir' }], property: 'capacity', label: 'feeds → Reservoir · capacity' },
    { source: 'state', states: ['dry'] },
  ],
  inState: 'flowing',
  where: [{ property: 'flow', op: '>', value: 5 }],
  sortKey: 'flow',
  sortDir: 'desc',
};

describe('the table a composition becomes', () => {
  it('is a roster of the kind with one computed column per path and state, sorted as chosen', () => {
    const table = tableOf(SPRINGS, 'Springs');
    expect(table.rows).toMatchObject({ kind: 'thingList', archetype: 'Spring', inState: 'flowing', where: SPRINGS.where });
    expect(table.columns.map((c) => c.key)).toEqual(['name', 'flow', 'path:feeds>Reservoir', 'path:feeds>Reservoir.capacity', 'state']);
    expect(table.sortKey).toBe('flow');
    expect(table.sortDir).toBe('desc');
    expect(table.rowDetail).toBe(true);
  });

  it('reads a path column through a related binding, its name or the property asked for', () => {
    const computed = (tableOf(SPRINGS, 'Springs').rows as { computed: { key: string; value: unknown }[] }).computed;
    expect(computed).toContainEqual({ key: 'path:feeds>Reservoir', value: { kind: 'related', via: SPRINGS.columns[1].source === 'path' ? SPRINGS.columns[1].steps : [] } });
    expect(computed).toContainEqual({ key: 'path:feeds>Reservoir.capacity', value: expect.objectContaining({ kind: 'related', property: 'capacity' }) });
    expect(computed).toContainEqual({ key: 'state', value: { kind: 'stateOf', states: ['dry'] } });
  });

  it('carries no narrowing it was not given', () => {
    const table = tableOf({ kind: 'Spring', columns: [] }, 'Springs');
    expect(table.rows).toEqual({ kind: 'thingList', archetype: 'Spring', computed: [] });
  });

  it('keys a path by every hop it takes, so two paths to one kind by different links stay two columns', () => {
    const viaFeeds = columnKey({ source: 'path', steps: [{ predicate: 'feeds', direction: 'out', archetype: 'Reservoir' }], label: '' });
    const viaDrains = columnKey({ source: 'path', steps: [{ predicate: 'drains', direction: 'in', archetype: 'Reservoir' }], label: '' });
    expect(viaFeeds).not.toBe(viaDrains);
  });

  it('counts one hop per step of a path and none for a property or a state', () => {
    expect(hopsOf({ source: 'path', steps: [{ predicate: 'a', direction: 'out' }, { predicate: 'b', direction: 'in' }], label: '' })).toBe(2);
    expect(hopsOf({ source: 'property', name: 'flow' })).toBe(0);
    expect(hopsOf({ source: 'state', states: ['dry'] })).toBe(0);
  });
});

describe('a composition read at a moment', () => {
  it('carries no state column and no state filter, since no read answers a state at an instant', () => {
    const table = tableOf({ ...SPRINGS, moment: '2026-09-01T00:00:00Z' }, 'Springs');
    expect(table.columns.map((c) => c.key)).not.toContain('state');
    expect(table.rows).not.toHaveProperty('inState');
  });
});

describe('a composition kept as a page', () => {
  it('is one section holding the table, and carries the composition it was made from', () => {
    const page = pageOf(SPRINGS, 'Springs by flow');
    expect(page.title).toBe('Springs by flow');
    expect(page.sections).toHaveLength(1);
    expect(page.sections[0].widgets[0]).toMatchObject({ type: 'table', title: 'Springs by flow' });
    expect(page.composed).toEqual(SPRINGS);
  });
});

describe('what the seed would refuse', () => {
  function thing(Id: string, Name: string, IsArchetype = false): VosThing {
    return { Id, Name, Properties: {}, IsArchetype };
  }
  function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
    return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
  }
  const index = buildModelIndex(
    [thing('is', 'is'), thing('feeds', 'feeds'), thing('spring', 'Spring', true), thing('reservoir', 'Reservoir', true), thing('s1', 'SPRING-1')],
    [relationship('i1', 's1', 'is', 'spring')],
  );
  const declared = new Set(['flowing', 'dry']);

  it('passes a composition naming only kinds, links and states the model declares', () => {
    expect(unresolvedNames(SPRINGS, index, declared)).toEqual([]);
  });

  it('names a kind the model no longer declares', () => {
    expect(unresolvedNames({ ...SPRINGS, kind: 'Wellhead' }, index, declared)).toEqual(['Wellhead']);
  });

  it('names a link or a far-end kind nothing declares, and a state no range derives', () => {
    const composition: Composition = {
      kind: 'Spring',
      columns: [
        { source: 'path', steps: [{ predicate: 'drains', direction: 'out', archetype: 'Basin' }], label: '' },
        { source: 'state', states: ['frozen'] },
      ],
      inState: 'thawed',
    };
    expect(unresolvedNames(composition, index, declared)).toEqual(['drains', 'Basin', 'frozen', 'thawed']);
  });
});
