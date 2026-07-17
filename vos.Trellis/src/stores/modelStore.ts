import { create } from 'zustand';
import type { VosThing, VosRelationship } from '../types/vos';
import { applyThingPropertyUpdate, applyRelationshipPropertyUpdate } from '../utils/propertyUpdates';

/** A coalesced batch of live-model changes applied in a single store write. */
export interface ModelBatch {
  thingUpserts?: VosThing[];
  thingRemovals?: string[];
  relationshipUpserts?: VosRelationship[];
  relationshipRemovals?: string[];
  thingPropertyUpdates?: { id: string; path: string; value: unknown }[];
  relationshipPropertyUpdates?: { id: string; name: string; value: unknown }[];
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
  /** Insert a thing, replacing any existing one with the same Id (idempotent). */
  upsertThing: (thing: VosThing) => void;
  /** Remove the thing with this Id, if present (no-op otherwise). */
  removeThing: (id: string) => void;
  /** Insert a relationship, replacing any existing one with the same Id (idempotent). */
  upsertRelationship: (relationship: VosRelationship) => void;
  /** Remove the relationship with this Id, if present (no-op otherwise). */
  removeRelationship: (id: string) => void;
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
  upsertThing: (thing) => set((s) => ({
    things: s.things.some((t) => t.Id === thing.Id)
      ? s.things.map((t) => (t.Id === thing.Id ? thing : t))
      : [...s.things, thing],
  })),
  removeThing: (id) => set((s) => ({ things: s.things.filter((t) => t.Id !== id) })),
  upsertRelationship: (relationship) => set((s) => ({
    relationships: s.relationships.some((r) => r.Id === relationship.Id)
      ? s.relationships.map((r) => (r.Id === relationship.Id ? relationship : r))
      : [...s.relationships, relationship],
  })),
  removeRelationship: (id) => set((s) => ({ relationships: s.relationships.filter((r) => r.Id !== id) })),
  applyBatch: (batch) => set((s) => {
    const next: Partial<ModelState> = {};

    const touchThings =
      batch.thingUpserts?.length || batch.thingRemovals?.length || batch.thingPropertyUpdates?.length;
    if (touchThings) {
      const map = new Map(s.things.map((t) => [t.Id, t]));
      for (const id of batch.thingRemovals ?? []) map.delete(id);
      for (const t of batch.thingUpserts ?? []) map.set(t.Id, t);
      for (const u of batch.thingPropertyUpdates ?? []) {
        const current = map.get(u.id);
        if (current) map.set(u.id, applyThingPropertyUpdate(current, u.path, u.value));
      }
      next.things = Array.from(map.values());
    }

    const touchRels =
      batch.relationshipUpserts?.length ||
      batch.relationshipRemovals?.length ||
      batch.relationshipPropertyUpdates?.length;
    if (touchRels) {
      const map = new Map(s.relationships.map((r) => [r.Id, r]));
      for (const id of batch.relationshipRemovals ?? []) map.delete(id);
      for (const r of batch.relationshipUpserts ?? []) map.set(r.Id, r);
      for (const u of batch.relationshipPropertyUpdates ?? []) {
        const current = map.get(u.id);
        if (current) map.set(u.id, applyRelationshipPropertyUpdate(current, u.name, u.value));
      }
      next.relationships = Array.from(map.values());
    }

    return next;
  }),
  markLoaded: () => set({ loaded: true }),
  clear: () => set({ things: [], relationships: [], loaded: false }),
}));
