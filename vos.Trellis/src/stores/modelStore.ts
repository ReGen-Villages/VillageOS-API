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
  /** Update things via a mapper function (for incremental updates). */
  updateThings: (updater: (prev: VosThing[]) => VosThing[]) => void;
  /** Update relationships via a mapper function. */
  updateRelationships: (updater: (prev: VosRelationship[]) => VosRelationship[]) => void;
  /** Remove the thing with this Id, if present (no-op otherwise). */
  /** Remove the relationship with this Id, if present (no-op otherwise). */
  /** Apply a coalesced batch of live changes in a single write (one array rebuild
   *  per collection instead of one per event). Used by the debounced SSE flush so a
   *  burst of hundreds of structural events costs O(N + batch), not O(N) per event. */
  applyBatch: (batch: ModelBatch) => void;
  /** Mark the store as loaded after initial fetch completes. */
  markLoaded: () => void;
  /** Clear all data (e.g., on model clear or logout). */
  clear: () => void;
}

export const useModelStore = create<ModelState>((set) => ({
  things: [],
  relationships: [],
  loaded: false,

  setThings: (things) => set({ things }),
  setRelationships: (relationships) => set({ relationships }),
  updateThings: (updater) => set((s) => ({ things: updater(s.things) })),
  updateRelationships: (updater) => set((s) => ({ relationships: updater(s.relationships) })),
  applyBatch: (batch) => set((s) => {
    const next: Partial<ModelState> = {};

    const touchThings =
      batch.thingUpserts?.length || batch.thingRemovals?.length ||
      batch.thingPropertyUpdates?.length || batch.thingPropertyRemovals?.length;
    if (touchThings) {
      const map = new Map(s.things.map((t) => [t.Id, t]));
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
      next.things = Array.from(map.values());
    }

    const touchRels =
      batch.relationshipUpserts?.length ||
      batch.relationshipRemovals?.length ||
      batch.relationshipPropertyUpdates?.length ||
      batch.relationshipPropertyRemovals?.length;
    if (touchRels) {
      const map = new Map(s.relationships.map((r) => [r.Id, r]));
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
      next.relationships = Array.from(map.values());
    }

    return next;
  }),
  markLoaded: () => set({ loaded: true }),
  clear: () => set({ things: [], relationships: [], loaded: false }),
}));
