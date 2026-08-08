import { describe, it, expect } from 'vitest';
import { sceneVisibilityFor, hiddenSceneItemsFor, type SceneVisibility } from './sceneVisibility';
import { NO_TYPE_ID } from '../../utils/typeFilter';
import type { VosThing, VosRelationship } from '../../types/vos';

function thing(id: string, name: string, ifcGlobalId?: string): VosThing {
  return { Id: id, Name: name, Properties: ifcGlobalId ? { ifcGlobalId } : {} };
}
function isRelation(id: string, subject: string, target: string): VosRelationship {
  return { Id: id, Name: '', SubjectId: subject, PredicateId: 'p-is', TargetId: target, Properties: {} };
}

// Two types with an instance each, plus an untyped Thing. The predicate and the
// type Things have no `is` relation of their own, so they land in the no-type
// bucket alongside it.
const isPredicate = thing('p-is', 'is');
const wallType = thing('t-wall', 'Wall', 'guid-wall-type');
const doorType = thing('t-door', 'Door', 'guid-door-type');
const wall = thing('i-wall', 'Wall-1', 'guid-wall');
const door = thing('i-door', 'Door-1', 'guid-door');
const untyped = thing('port', 'Port-1', 'guid-port');

const things = [isPredicate, wallType, doorType, wall, door, untyped];
const relationships = [isRelation('r1', 'i-wall', 't-wall'), isRelation('r2', 'i-door', 't-door')];
const everyBucket = new Set(['t-wall', 't-door', NO_TYPE_ID]);

describe('sceneVisibilityFor (Bug #5366)', () => {
  it('shows everything when nothing is hidden', () => {
    expect(sceneVisibilityFor(things, relationships, new Set())).toEqual({ kind: 'everything' });
  });

  // The heart of the bug: naming guids to hide can only reach elements the model
  // has a Thing for, and a real Fragments artifact holds far more than that.
  it('shows nothing when the filter leaves no Thing standing', () => {
    expect(sceneVisibilityFor(things, relationships, everyBucket)).toEqual({ kind: 'nothing' });
  });

  it('hides instances, the type Things themselves, and untyped Things on a partial filter', () => {
    const result = sceneVisibilityFor(things, relationships, new Set(['t-wall', NO_TYPE_ID]));
    expect(result.kind).toBe('everythingExcept');
    const guids = result.kind === 'everythingExcept' ? [...result.hiddenIfcGuids].sort() : [];
    expect(guids).toEqual(['guid-door-type', 'guid-port', 'guid-wall', 'guid-wall-type']);
  });

  it('leaves a Thing without an IFC identifier out of the hide list', () => {
    const noGuid = [isPredicate, wallType, thing('i-plain', 'No geometry')];
    const result = sceneVisibilityFor(noGuid, [], new Set([NO_TYPE_ID]));
    expect(result).toEqual({ kind: 'nothing' });
  });

  // A stale id cannot blank the scene: it hides nothing, so Things still stand.
  it('ignores a hidden id that names no bucket', () => {
    const result = sceneVisibilityFor(things, relationships, new Set(['t-gone']));
    expect(result.kind).toBe('everythingExcept');
    expect(result.kind === 'everythingExcept' && result.hiddenIfcGuids).toEqual([]);
  });

  // Nothing to have hidden, so the scene is left alone rather than blanked —
  // a .frag can hold geometry when the model holds no Things at all.
  it('does not report nothing for a model with no Things', () => {
    expect(sceneVisibilityFor([], [], everyBucket)).toEqual({ kind: 'everythingExcept', hiddenIfcGuids: [] });
  });
});

describe('hiddenSceneItemsFor (Bug #5366)', () => {
  function fakeScene(showing: number[], byGuid: Record<string, number | null>) {
    const calls: string[] = [];
    return {
      calls,
      getItemsByVisibility: async (visible: boolean) => {
        calls.push(`getItemsByVisibility(${visible})`);
        return visible ? showing : [];
      },
      getLocalIdsByGuids: async (guids: string[]) => {
        calls.push(`getLocalIdsByGuids(${guids.length})`);
        return guids.map((g) => byGuid[g] ?? null);
      },
    };
  }

  it('hides nothing when everything should show', async () => {
    const scene = fakeScene([1, 2, 3], {});
    expect(await hiddenSceneItemsFor({ kind: 'everything' }, scene)).toEqual([]);
    expect(scene.calls).toEqual([]);
  });

  // The regression guard. Asking the scene what it is showing reaches elements
  // the model has no Thing for; translating guids does not, which is what left
  // the model on screen. The e2e test proves this against a real .frag, but it
  // needs a live broker, so this is the one that runs in the pipeline.
  it('hides what the scene is showing when nothing should show', async () => {
    const scene = fakeScene([7, 8, 9], { 'guid-a': 7 });
    expect(await hiddenSceneItemsFor({ kind: 'nothing' }, scene)).toEqual([7, 8, 9]);
    expect(scene.calls).toEqual(['getItemsByVisibility(true)']);
  });

  it('translates the named guids on a partial hide', async () => {
    const scene = fakeScene([1, 2, 3], { 'guid-a': 11, 'guid-b': 22 });
    const visibility: SceneVisibility = { kind: 'everythingExcept', hiddenIfcGuids: ['guid-a', 'guid-b'] };
    expect(await hiddenSceneItemsFor(visibility, scene)).toEqual([11, 22]);
    expect(scene.calls).toEqual(['getLocalIdsByGuids(2)']);
  });

  // Most of the model's identifiers match no scene item (Bug #6191); the ones
  // that resolve to nothing must not reach setVisible as undefined entries.
  it('drops guids the scene cannot resolve', async () => {
    const scene = fakeScene([1], { 'guid-a': 11 });
    const visibility: SceneVisibility = { kind: 'everythingExcept', hiddenIfcGuids: ['guid-a', 'guid-missing'] };
    expect(await hiddenSceneItemsFor(visibility, scene)).toEqual([11]);
  });

  it('asks the scene nothing when the hide list is empty', async () => {
    const scene = fakeScene([1, 2], {});
    const visibility: SceneVisibility = { kind: 'everythingExcept', hiddenIfcGuids: [] };
    expect(await hiddenSceneItemsFor(visibility, scene)).toEqual([]);
    expect(scene.calls).toEqual([]);
  });
});
