import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useModelStore, useThingStates } from './modelStore';
import type { VosThing, VosRelationship } from '../types/vos';

const thing = (id: string, name: string) => ({
  Id: id, Name: name, Properties: {}, InheritedOverrides: {},
}) as unknown as VosThing;

const rel = (id: string, name: string) => ({
  Id: id, Name: name, SubjectId: 's', PredicateId: 'p', TargetId: 't',
  SubjectName: 'S', PredicateName: 'P', TargetName: 'T', Properties: {},
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

  // Bug #6143 — a batch could set a property but never take one away, so a deleted property stayed
  // in the store until the whole model was reloaded.
  it('applyBatch removes a property from a thing and a relationship', () => {
    useModelStore.getState().setThings([{ ...thing('1', 'A'), Properties: { keep: 1, drop: 2 } }]);
    useModelStore.getState().setRelationships([{ ...rel('r1', 'X'), Properties: { keep: 1, drop: 2 } }]);

    useModelStore.getState().applyBatch({
      thingPropertyRemovals: [{ id: '1', path: 'drop' }],
      relationshipPropertyRemovals: [{ id: 'r1', name: 'drop' }],
    });

    expect(useModelStore.getState().things[0].Properties).toEqual({ keep: 1 });
    expect(useModelStore.getState().relationships[0].Properties).toEqual({ keep: 1 });
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

// A flush used to rebuild the whole index to change one Thing — a Map over every Thing in the
// model, then a fresh array out of it — so its cost grew with the model however small the batch.
// The index is kept and mutated instead, and what is asserted is how many entries a batch writes
// into it: a count is stable where a stopwatch on a build agent is not.
describe('a flush costs the size of the batch, not the size of the model', () => {
  const MODEL_SIZE = 50_000;
  const aModelOf = (count: number) => Array.from({ length: count }, (_, i) => thing(`T${i}`, `t${i}`));
  const edgesOf = (count: number) => Array.from({ length: count }, (_, i) => rel(`R${i}`, `r${i}`));

  let indexWrites: ReturnType<typeof vi.spyOn>;
  beforeEach(() => { indexWrites = vi.spyOn(Map.prototype, 'set'); });
  afterEach(() => { indexWrites.mockRestore(); });

  it('changes one Thing in a large model without touching the rest of the index', () => {
    useModelStore.getState().setThings(aModelOf(MODEL_SIZE));
    useModelStore.getState().applyBatch({ thingUpserts: [thing('T7', 'first')] });
    indexWrites.mockClear();

    useModelStore.getState().applyBatch({ thingUpserts: [thing('T7', 'renamed')] });

    expect(indexWrites).toHaveBeenCalledTimes(1);
    expect(useModelStore.getState().things).toHaveLength(MODEL_SIZE);
    expect(useModelStore.getState().things.find((t) => t.Id === 'T7')?.Name).toBe('renamed');
  });

  it('changes one relationship in a large model without touching the rest of the index', () => {
    useModelStore.getState().setRelationships(edgesOf(MODEL_SIZE));
    useModelStore.getState().applyBatch({ relationshipUpserts: [rel('R7', 'first')] });
    indexWrites.mockClear();

    useModelStore.getState().applyBatch({ relationshipUpserts: [rel('R7', 'renamed')] });

    expect(indexWrites).toHaveBeenCalledTimes(1);
    expect(useModelStore.getState().relationships).toHaveLength(MODEL_SIZE);
  });

  // A kept index invites the fault where something writes the store without going through its
  // actions and the index quietly disagrees with what the page draws. The hook's own test does
  // exactly that through setState, so the index is rebuilt once against an array it does not know.
  it('re-indexes once after the store was written round the back, then keeps the index', () => {
    useModelStore.getState().setThings(aModelOf(10));
    useModelStore.getState().applyBatch({ thingUpserts: [thing('T7', 'first')] });
    useModelStore.setState({ things: [thing('X1', 'x1'), thing('X2', 'x2'), thing('X3', 'x3')] });
    indexWrites.mockClear();

    useModelStore.getState().applyBatch({ thingUpserts: [thing('X2', 'renamed')] });
    expect(indexWrites).toHaveBeenCalledTimes(3 + 1);
    expect(useModelStore.getState().things.map((t) => t.Id)).toEqual(['X1', 'X2', 'X3']);
    expect(useModelStore.getState().things.find((t) => t.Id === 'X2')?.Name).toBe('renamed');

    indexWrites.mockClear();
    useModelStore.getState().applyBatch({ thingUpserts: [thing('X2', 'again')] });
    expect(indexWrites).toHaveBeenCalledTimes(1);
  });

  it('drops the index at a load, so the replaced model is released before the next flush', () => {
    const cleared = vi.spyOn(Map.prototype, 'clear');
    useModelStore.getState().setThings(aModelOf(10));
    useModelStore.getState().applyBatch({ thingUpserts: [thing('T7', 'first')] });
    cleared.mockClear();

    useModelStore.getState().setThings(aModelOf(3));
    expect(cleared).toHaveBeenCalledTimes(1);

    useModelStore.getState().setRelationships(edgesOf(3));
    useModelStore.getState().applyBatch({ relationshipUpserts: [rel('R1', 'first')] });
    cleared.mockClear();
    useModelStore.getState().clear();
    expect(cleared).toHaveBeenCalledTimes(2);
    cleared.mockRestore();
  });
});

describe('the derived states beside the model', () => {
  const held = [thing('T1', 'one'), thing('T2', 'two')];

  it('seeds from a snapshot, replacing what was there', () => {
    useModelStore.getState().seedThingStates(new Map([['T1', ['flagged']]]));
    useModelStore.getState().seedThingStates(new Map([['T2', ['metered']]]));

    expect(useModelStore.getState().thingStates.get('T1')).toBeUndefined();
    expect(useModelStore.getState().thingStates.get('T2')).toEqual(['metered']);
  });

  it('writes a state change in place and moves the version', () => {
    useModelStore.getState().setThings(held);
    useModelStore.getState().seedThingStates(new Map([['T1', ['flagged']]]));
    const map = useModelStore.getState().thingStates;
    const version = useModelStore.getState().thingStatesVersion;

    useModelStore.getState().applyBatch({ thingStateUpdates: [{ id: 'T1', states: ['cleared'] }] });

    expect(useModelStore.getState().thingStates).toBe(map);
    expect(map.get('T1')).toEqual(['cleared']);
    expect(useModelStore.getState().thingStatesVersion).toBe(version + 1);
  });

  it('keeps a state change only for a Thing the store holds', () => {
    useModelStore.getState().setThings(held);

    useModelStore.getState().applyBatch({ thingStateUpdates: [{ id: 'T9', states: ['flagged'] }] });

    expect(useModelStore.getState().thingStates.has('T9')).toBe(false);
  });

  it('drops the states at a load and on clear', () => {
    useModelStore.getState().seedThingStates(new Map([['T1', ['flagged']]]));
    useModelStore.getState().setThings(held);
    expect(useModelStore.getState().thingStates.size).toBe(0);

    useModelStore.getState().seedThingStates(new Map([['T1', ['flagged']]]));
    useModelStore.getState().clear();
    expect(useModelStore.getState().thingStates.size).toBe(0);
  });

  it('redraws a component reading the states when one moves', () => {
    useModelStore.getState().setThings(held);
    useModelStore.getState().seedThingStates(new Map([['T1', ['flagged']]]));
    let renders = 0;
    const { result } = renderHook(() => { renders += 1; return useThingStates(); });
    const before = renders;

    act(() => { useModelStore.getState().applyBatch({ thingStateUpdates: [{ id: 'T1', states: ['cleared'] }] }); });

    expect(renders).toBeGreaterThan(before);
    expect(result.current.get('T1')).toEqual(['cleared']);
  });
});
