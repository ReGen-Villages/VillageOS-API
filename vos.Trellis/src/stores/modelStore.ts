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
  markLoaded: () => set({ loaded: true }),
  clear: () => set({ things: [], relationships: [], loaded: false }),
}));
