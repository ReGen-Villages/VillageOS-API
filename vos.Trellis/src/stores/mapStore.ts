import { create } from 'zustand';
import type { BasemapSource } from '../types/basemap';

/**
 * Map state, kept in its own store rather than in the shared UI store. The map view removed in
 * pull request 292 spread its state through the global store and had to be deleted whole to get
 * it back out.
 */
interface MapState {
  /** The layer the reader chose, by name — ids are not stable across a model switch. */
  selectedSourceName: string | null;
  /** True once the tile source has failed to answer, so the page can say so instead of showing blank ground. */
  tilesUnreachable: boolean;
  selectSource: (name: string) => void;
  reportTilesUnreachable: () => void;
}

export const useMapStore = create<MapState>((set) => ({
  selectedSourceName: null,
  tilesUnreachable: false,
  selectSource: (name) => set({ selectedSourceName: name, tilesUnreachable: false }),
  reportTilesUnreachable: () => set({ tilesUnreachable: true }),
}));

export function resolveSelectedSource(
  sources: BasemapSource[],
  selectedName: string | null,
): BasemapSource | null {
  return sources.find((s) => s.name === selectedName) ?? sources[0] ?? null;
}
