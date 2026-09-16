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
}

interface ModelState {
  things: VosThing[];
  relationships: VosRelationship[];
  /** True after first successful load (phased or full). */
  loaded: boolean;

  setThings: (things: VosThing[]) => void;
  setRelationships: (relationships: VosRelationship[]) => void;
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
  loaded: false,

  // A load replaces the model, so the index is dropped rather than left holding the last one until
  // the next flush rebuilds it — on a switch between models, the whole of the old one.
  setThings: (things) => {
    thingsById.forget();
    set({ things });
  },
  setRelationships: (relationships) => {
    relationshipsById.forget();
    set({ relationships });
  },
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

    return next;
  }),
  markLoaded: () => set({ loaded: true }),
  clear: () => {
    thingsById.forget();
    relationshipsById.forget();
    set({ things: [], relationships: [], loaded: false });
  },
}));
