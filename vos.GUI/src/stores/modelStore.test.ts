import { describe, it, expect, beforeEach } from 'vitest';
import { useModelStore } from './modelStore';
import type { VosThing, VosRelationship } from '../types/vos';

const thing = (id: string, name: string): VosThing => ({
  Id: id, Name: name, OwnProperties: {}, InheritedProperties: {},
});

const rel = (id: string, name: string): VosRelationship => ({
  Id: id, Name: name, SubjectId: 's', PredicateId: 'p', TargetId: 't',
  SubjectName: 'S', PredicateName: 'P', TargetName: 'T', OwnProperties: {},
});

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
