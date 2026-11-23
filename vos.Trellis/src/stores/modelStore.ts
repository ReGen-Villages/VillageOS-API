import { create } from 'zustand';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import {
  applyThingPropertyUpdate,
  applyRelationshipPropertyUpdate,
  applyThingPropertyRemoval,
  applyRelationshipPropertyRemoval,
} from '../utils/propertyUpdates';

interface ModelBatch {
  thingUpserts?: VosThing[];
  thingRemovals?: string[];
  relationshipUpserts?: VosRelationship[];
  relationshipRemovals?: string[];
  thingPropertyUpdates?: { id: string; path: string; value: unknown }[];
  relationshipPropertyUpdates?: { id: string; name: string; value: unknown }[];
  thingPropertyRemovals?: { id: string; path: string }[];
  relationshipPropertyRemovals?: { id: string; name: string }[];
  /** The whole set of states a Thing now holds — never a delta — so the newest applied wins
   *  whatever order they arrived in. */
  thingStateUpdates?: { id: string; states: string[] }[];
}

interface ModelState {
  things: VosThing[];
  relationships: VosRelationship[];
  /** Beside the model rather than on it, because states change far more often than the model's shape, and
   *  folding one into `things` would rebuild that array and every index built from it. Written in place,
   *  as the index is; the version beside it is what says it moved. */
  thingStates: Map<string, string[]>;
  thingStatesVersion: number;
  /** How many times a load has replaced what is held. A live batch changes what is held without
   *  replacing it, and does not count. */
  loadsApplied: number;
  loaded: boolean;
  /** Whether what is held is the whole model rather than one page's own set of Things. A figure
   *  about the model is drawn only while this is true: a page's set counted as the model is a
   *  wrong figure, not a partial one. */
  holdsWholeModel: boolean;

  setThings: (things: VosThing[]) => void;
  setRelationships: (relationships: VosRelationship[]) => void;
  /** Replace every state with what a fresh snapshot holds. Replaces rather than merges: a Thing
   *  that has left its last state has no entry in the snapshot, and merging would leave it holding
   *  that state for as long as the session lasted. */
  seedThingStates: (thingStates: Map<string, string[]>) => void;
  /** The kept index makes a batch cost its own size plus one copy of the array's references, whatever
   *  the size of the model. */
  applyBatch: (batch: ModelBatch) => void;
  markLoaded: (load: { wholeModel: boolean }) => void;
  clear: () => void;
}

/**
 * Kept by identifier outside the store and changed in place, so a flush costs the size of the batch, not a
 * rebuild over the whole model; in the state it would have to be copied to change. The arrays are rebuilt
 * from it each flush, because a new array is what tells React something changed, and copying references is cheap.
 */
class KeptById<T extends { Id: string }> {
  private readonly index = new Map<string, T>();

  /** Anything writing the store past its actions, such as `setState` in a test, leaves a different array,
   *  and one comparison catches it before the index disagrees with what the page draws. */
  private builtFrom: readonly T[] | null = null;

  forThe(entries: readonly T[]): Map<string, T> {
    if (this.builtFrom === entries) return this.index;
    this.index.clear();
    for (const entry of entries) this.index.set(entry.Id, entry);
    this.builtFrom = entries;
    return this.index;
  }

  asArray(): T[] {
    const entries = [...this.index.values()];
    this.builtFrom = entries;
    return entries;
  }

  forget(): void {
    this.index.clear();
    this.builtFrom = null;
  }
}

const thingsById = new KeptById<VosThing>();
const relationshipsById = new KeptById<VosRelationship>();

/** True when a removed Thing took its states with it. */
function applyThingChanges(batch: ModelBatch, s: ModelState, next: Partial<ModelState>): boolean {
  if (!(batch.thingUpserts?.length || batch.thingRemovals?.length
    || batch.thingPropertyUpdates?.length || batch.thingPropertyRemovals?.length)) return false;

  let statesMoved = false;
  const map = thingsById.forThe(s.things);
  for (const id of batch.thingRemovals ?? []) {
    map.delete(id);
    if (s.thingStates.delete(id)) statesMoved = true;
  }
  for (const t of batch.thingUpserts ?? []) map.set(t.Id, t);
  for (const u of batch.thingPropertyUpdates ?? []) {
    const current = map.get(u.id);
    if (current) map.set(u.id, applyThingPropertyUpdate(current, u.path, u.value));
  }
  for (const r of batch.thingPropertyRemovals ?? []) {
    const current = map.get(r.id);
    if (current) map.set(r.id, applyThingPropertyRemoval(current, r.path));
  }
  next.things = thingsById.asArray();
  return statesMoved;
}

function applyRelationshipChanges(batch: ModelBatch, s: ModelState, next: Partial<ModelState>): void {
  if (!(batch.relationshipUpserts?.length || batch.relationshipRemovals?.length
    || batch.relationshipPropertyUpdates?.length || batch.relationshipPropertyRemovals?.length)) return;

  const map = relationshipsById.forThe(s.relationships);
  for (const id of batch.relationshipRemovals ?? []) map.delete(id);
  for (const r of batch.relationshipUpserts ?? []) map.set(r.Id, r);
  for (const u of batch.relationshipPropertyUpdates ?? []) {
    const current = map.get(u.id);
    if (current) map.set(u.id, applyRelationshipPropertyUpdate(current, u.name, u.value));
  }
  for (const r of batch.relationshipPropertyRemovals ?? []) {
    const current = map.get(r.id);
    if (current) map.set(r.id, applyRelationshipPropertyRemoval(current, r.name));
  }
  next.relationships = relationshipsById.asArray();
}

// The events stream carries every Thing's state changes, so a change to a Thing the store does
// not hold is dropped rather than kept for no reader.
function applyStateChanges(batch: ModelBatch, s: ModelState, next: Partial<ModelState>): boolean {
  if (!batch.thingStateUpdates?.length) return false;
  const held = thingsById.forThe(next.things ?? s.things);
  for (const u of batch.thingStateUpdates) if (held.has(u.id)) s.thingStates.set(u.id, u.states);
  return true;
}

export const useModelStore = create<ModelState>((set) => ({
  things: [],
  relationships: [],
  thingStates: new Map(),
  thingStatesVersion: 0,
  loadsApplied: 0,
  loaded: false,
  holdsWholeModel: false,

  // A load replaces the model, so the index is dropped rather than left holding the last one until
  // the next flush rebuilds it — on a switch between models, the whole of the old one. The states
  // go with it: what the load replaces, the snapshot beside it re-seeds.
  setThings: (things) => {
    thingsById.forget();
    set((s) => ({
      things, thingStates: new Map(), thingStatesVersion: s.thingStatesVersion + 1, loadsApplied: s.loadsApplied + 1,
    }));
  },
  setRelationships: (relationships) => {
    relationshipsById.forget();
    set({ relationships });
  },
  seedThingStates: (thingStates) =>
    set((s) => ({ thingStates, thingStatesVersion: s.thingStatesVersion + 1 })),
  applyBatch: (batch) => set((s) => {
    const next: Partial<ModelState> = {};
    const removalsMovedStates = applyThingChanges(batch, s, next);
    applyRelationshipChanges(batch, s, next);
    const updatesMovedStates = applyStateChanges(batch, s, next);
    if (removalsMovedStates || updatesMovedStates) next.thingStatesVersion = s.thingStatesVersion + 1;
    return next;
  }),
  markLoaded: ({ wholeModel }) => set({ loaded: true, holdsWholeModel: wholeModel }),
  clear: () => {
    thingsById.forget();
    relationshipsById.forget();
    set((s) => ({
      things: [], relationships: [], loaded: false, holdsWholeModel: false,
      thingStates: new Map(), thingStatesVersion: s.thingStatesVersion + 1,
    }));
  },
}));

/** The map is written in place and never changes identity, so a component selecting only it would draw the
 *  states once; watching the version beside it redraws the caller. Both are read here together, because
 *  apart, either looks removable and nothing would fail when it went. */
export function useThingStates(): Map<string, string[]> {
  useModelStore((s) => s.thingStatesVersion);
  return useModelStore.getState().thingStates;
}
