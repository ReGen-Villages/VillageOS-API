import { describe, it, expect, beforeEach } from 'vitest';
import { useModelStore } from './modelStore';
import type { VosThing, VosRelationship } from '../types/vos';

const thing = (id: string, name: string) => ({
  Id: id, Name: name, Properties: {}, OwnProperties: {}, InheritedProperties: {},
}) as unknown as VosThing;

const rel = (id: string, name: string) => ({
  Id: id, Name: name, SubjectId: 's', PredicateId: 'p', TargetId: 't',
  SubjectName: 'S', PredicateName: 'P', TargetName: 'T', Properties: {}, OwnProperties: {},
}) as unknown as VosRelationship;

beforeEach(() => {
  useModelStore.getState().clear();
});

describe('modelStore', () => {
  it('starts empty', () => {
    const { things, relationships, loaded } = useModelStore.getState();
    expect(things).toEqual([]);
    expect(relationships).toEqual([]);
    expect(loaded).toBe(false);
  });

  it('setThings replaces the things array', () => {
    useModelStore.getState().setThings([thing('1', 'A'), thing('2', 'B')]);
    expect(useModelStore.getState().things).toHaveLength(2);
  });

  it('setRelationships replaces the relationships array', () => {
    useModelStore.getState().setRelationships([rel('r1', 'X')]);
    expect(useModelStore.getState().relationships).toHaveLength(1);
  });

  it('updateThings applies an updater function', () => {
    useModelStore.getState().setThings([thing('1', 'A')]);
    useModelStore.getState().updateThings((prev) => [...prev, thing('2', 'B')]);
    expect(useModelStore.getState().things).toHaveLength(2);
    expect(useModelStore.getState().things[1].Name).toBe('B');
  });

  it('updateRelationships applies an updater function', () => {
    useModelStore.getState().setRelationships([rel('r1', 'X')]);
    useModelStore.getState().updateRelationships((prev) => prev.filter((r) => r.Id !== 'r1'));
    expect(useModelStore.getState().relationships).toHaveLength(0);
  });

  it('markLoaded sets loaded to true', () => {
    useModelStore.getState().markLoaded();
    expect(useModelStore.getState().loaded).toBe(true);
  });

  it('applyBatch upserts, removes, and mutates a property in one write', () => {
    useModelStore.getState().setThings([thing('1', 'A'), thing('2', 'B')]);
    useModelStore.getState().setRelationships([rel('r1', 'X')]);

    let writes = 0;
    const unsub = useModelStore.subscribe(() => { writes++; });
    useModelStore.getState().applyBatch({
      thingUpserts: [thing('3', 'C')],
      thingRemovals: ['1'],
      thingPropertyUpdates: [{ id: '2', path: 'geometry', value: 'g2' }],
      relationshipUpserts: [rel('r2', 'Y')],
      relationshipRemovals: ['r1'],
    });
    unsub();

    expect(writes).toBe(1); // a whole batch is a single store write
    expect(useModelStore.getState().things.map((t) => t.Id).sort()).toEqual(['2', '3']);
    expect(useModelStore.getState().things.find((t) => t.Id === '2')?.Properties?.geometry).toBe('g2');
    expect(useModelStore.getState().relationships.map((r) => r.Id)).toEqual(['r2']);
  });

  it('applyBatch upsert of an existing id replaces in place (idempotent)', () => {
    useModelStore.getState().setThings([thing('1', 'A')]);
    useModelStore.getState().applyBatch({ thingUpserts: [thing('1', 'A-renamed')] });
    expect(useModelStore.getState().things).toHaveLength(1);
    expect(useModelStore.getState().things[0].Name).toBe('A-renamed');
  });

  it('applyBatch leaves a collection untouched when it has no changes', () => {
    const rels = [rel('r1', 'X')];
    useModelStore.getState().setThings([thing('1', 'A')]);
    useModelStore.getState().setRelationships(rels);
    useModelStore.getState().applyBatch({ thingUpserts: [thing('2', 'B')] });
    // relationships not in the batch → same array reference preserved (no needless rebuild).
    expect(useModelStore.getState().relationships).toBe(rels);
  });

  it('clear resets everything', () => {
    useModelStore.getState().setThings([thing('1', 'A')]);
    useModelStore.getState().setRelationships([rel('r1', 'X')]);
    useModelStore.getState().markLoaded();
    useModelStore.getState().clear();
    const { things, relationships, loaded } = useModelStore.getState();
    expect(things).toEqual([]);
    expect(relationships).toEqual([]);
    expect(loaded).toBe(false);
  });
});
