import { create } from 'zustand';
import type { VosThing, VosRelationship } from '../types/vos';

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
  markLoaded: () => set({ loaded: true }),
  clear: () => set({ things: [], relationships: [], loaded: false }),
}));
