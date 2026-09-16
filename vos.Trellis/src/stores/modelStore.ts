import { create } from 'zustand';
import type { VosThing, VosRelationship } from '../types/vos';
import {
  applyThingPropertyUpdate,
  applyRelationshipPropertyUpdate,
  applyThingPropertyRemoval,
  applyRelationshipPropertyRemoval,
} from '../utils/propertyUpdates';

/** A coalesced batch of live-model changes applied in a single store write. */
export interface ModelBatch {
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
  /** The derived states each held Thing currently holds, kept beside the model rather than on it. A
   *  state moves far more often than the model's shape does, and folding one into `things` would
   *  rebuild that array — and every index built from it — on every state change. Written in place
   *  for the same reason the index is; the version beside it is what says it moved. */
  thingStates: Map<string, string[]>;
  thingStatesVersion: number;
  /** True after first successful load (phased or full). */
  loaded: boolean;

  setThings: (things: VosThing[]) => void;
  setRelationships: (relationships: VosRelationship[]) => void;
  /** Replace every state with what a fresh snapshot holds. Replaces rather than merges: a Thing
   *  that has left its last state has no entry in the snapshot, and merging would leave it holding
   *  that state for as long as the session lasted. */
  seedThingStates: (thingStates: Map<string, string[]>) => void;
  /** Apply a coalesced batch of live changes in a single write. Used by the debounced SSE flush;
   *  the kept index makes a batch cost its own size plus one copy of the array's references,
   *  whatever the size of the model. */
  applyBatch: (batch: ModelBatch) => void;
  /** Mark the store as loaded after initial fetch completes. */
  markLoaded: () => void;
  /** Clear all data (e.g., on model clear or logout). */
  clear: () => void;
}

/**
 * The model kept by id, so a flush changes what the batch names instead of walking everything to
 * find it. Rebuilding this on every flush was the whole cost of applying one, and it grew with the
 * model rather than with the batch. It is held outside the store and mutated in place: putting it
 * in the state would mean copying it to change it, which is the cost being removed.
 *
 * The arrays stay the store's own shape. Every page reads `things` and `relationships` and a new
 * array is what tells React something changed, so they are rebuilt from the index each flush — a
 * copy of the references, cheap where building the index is not.
 */
class KeptById<T extends { Id: string }> {
  private readonly index = new Map<string, T>();

  /** The array this index was last built from. Anything that writes the store without going
   *  through its actions — `setState` in a test, and whatever does it next — leaves a different
   *  array behind, and comparing the two references catches that for the price of one comparison.
   *  The alternative is an index that quietly disagrees with what the page is drawing. */
  private builtFrom: readonly T[] | null = null;

  forThe(entries: readonly T[]): Map<string, T> {
    if (this.builtFrom === entries) return this.index;
    this.index.clear();
    for (const entry of entries) this.index.set(entry.Id, entry);
    this.builtFrom = entries;
    return this.index;
  }

  /** What the index now holds, as the array the store keeps and the pages read. */
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

export const useModelStore = create<ModelState>((set) => ({
  things: [],
  relationships: [],
  thingStates: new Map(),
  thingStatesVersion: 0,
  loaded: false,

  // A load replaces the model, so the index is dropped rather than left holding the last one until
  // the next flush rebuilds it — on a switch between models, the whole of the old one. The states
  // go with it: what the load replaces, the snapshot beside it re-seeds.
  setThings: (things) => {
    thingsById.forget();
    set((s) => ({ things, thingStates: new Map(), thingStatesVersion: s.thingStatesVersion + 1 }));
  },
  setRelationships: (relationships) => {
    relationshipsById.forget();
    set({ relationships });
  },
  seedThingStates: (thingStates) =>
    set((s) => ({ thingStates, thingStatesVersion: s.thingStatesVersion + 1 })),
  applyBatch: (batch) => set((s) => {
    const next: Partial<ModelState> = {};

    const touchThings =
      batch.thingUpserts?.length || batch.thingRemovals?.length ||
      batch.thingPropertyUpdates?.length || batch.thingPropertyRemovals?.length;
    if (touchThings) {
      const map = thingsById.forThe(s.things);
      for (const id of batch.thingRemovals ?? []) map.delete(id);
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
    }

    const touchRels =
      batch.relationshipUpserts?.length ||
      batch.relationshipRemovals?.length ||
      batch.relationshipPropertyUpdates?.length ||
      batch.relationshipPropertyRemovals?.length;
    if (touchRels) {
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
    if (batch.thingStateUpdates?.length) {
      const held = thingsById.forThe(next.things ?? s.things);
      for (const u of batch.thingStateUpdates) if (held.has(u.id)) s.thingStates.set(u.id, u.states);
      next.thingStatesVersion = s.thingStatesVersion + 1;
    }

    return next;
  }),
  markLoaded: () => set({ loaded: true }),
  clear: () => {
    thingsById.forget();
    relationshipsById.forget();
    set((s) => ({
      things: [], relationships: [], loaded: false,
      thingStates: new Map(), thingStatesVersion: s.thingStatesVersion + 1,
    }));
  },
}));

/**
 * The states as they stand, for a component that draws them.
 *
 * Reading the map is not enough on its own: it is written in place, so its identity never changes
 * and a component that only selected it would draw the states once and never again — and nothing
 * about that read would look wrong. The version beside it is what moves, so watching that is what
 * redraws the caller. The two belong in one expression: written apart, the watch reads as a line
 * with no effect and the read reads as complete, and either can be removed without anything failing.
 */
export function useThingStates(): Map<string, string[]> {
  useModelStore((s) => s.thingStatesVersion);
  return useModelStore.getState().thingStates;
}
