import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';

// ── localStorage polyfill ──────────────────────────────────────────────────
if (!globalThis.localStorage || typeof globalThis.localStorage.getItem !== 'function') {
  const store: Record<string, string> = {};
  Object.defineProperty(globalThis, 'localStorage', {
    value: {
      getItem: (key: string) => store[key] ?? null,
      setItem: (key: string, val: string) => { store[key] = val; },
      removeItem: (key: string) => { delete store[key]; },
      clear: () => { for (const k of Object.keys(store)) delete store[k]; },
      get length() { return Object.keys(store).length; },
      key: (i: number) => Object.keys(store)[i] ?? null,
    },
    writable: true,
    configurable: true,
  });
}

// ── Test fixtures ──────────────────────────────────────────────────────────

const surfaceThing: VosThing = {
  Id: 'thing-1',
  Name: 'Building A',
  Properties: { latitude: 40.7, longitude: -74.0 },
};

const remainingThing: VosThing = {
  Id: 'thing-2',
  Name: 'Sensor',
  Properties: {},
};

const relationship: VosRelationship = {
  Id: 'rel-1',
  Name: 'contains',
  SubjectId: 'thing-1',
  TargetId: 'thing-2',
  PredicateId: 'pred-contains',
  Properties: {},
};

// ── Mocks ──────────────────────────────────────────────────────────────────

vi.mock('../api/thingApi', () => ({
  thingApi: {
    getAll: vi.fn(),
    getSurfaceThings: vi.fn(),
    getRemainingThings: vi.fn(),
  },
}));

vi.mock('../api/relationshipApi', () => ({
  relationshipApi: {
    getAll: vi.fn(),
  },
}));

const { thingApi } = await import('../api/thingApi');
const { relationshipApi } = await import('../api/relationshipApi');
const { useModelStore } = await import('../stores/modelStore');
const { useUiStore } = await import('../stores/uiStore');
const { _resetLoadGuard } = await import('./useModelLoader');

// ── Helpers ────────────────────────────────────────────────────────────────

function resetStores() {
  useModelStore.setState({ things: [], relationships: [], loaded: false });
  useUiStore.setState({ loadingPhase: 'idle' });
  _resetLoadGuard();
}

// ── Tests ──────────────────────────────────────────────────────────────────

describe('useModelLoader (phased loading)', () => {
  beforeEach(() => {
    resetStores();
    vi.clearAllMocks();
  });

  describe('phased loading populates modelStore', () => {
    it('phase 1 sets surface things + relationships', async () => {
      vi.mocked(thingApi.getSurfaceThings).mockResolvedValue([surfaceThing]);
      vi.mocked(relationshipApi.getAll).mockResolvedValue([relationship]);
      vi.mocked(thingApi.getRemainingThings).mockResolvedValue([remainingThing]);

      // Dynamically import to trigger the module (avoids stale hook caching)
      const { loadModelPhased } = await import('./useModelLoader');
      await loadModelPhased();

      const state = useModelStore.getState();
      expect(state.things.length).toBe(2);
      expect(state.relationships.length).toBe(1);
      expect(state.loaded).toBe(true);
      expect(useUiStore.getState().loadingPhase).toBe('done');
    });

    it('falls back to full load when phase endpoints fail', async () => {
      vi.mocked(thingApi.getSurfaceThings).mockRejectedValue(new Error('404'));
      vi.mocked(thingApi.getAll).mockResolvedValue([surfaceThing, remainingThing]);
      vi.mocked(relationshipApi.getAll).mockResolvedValue([relationship]);

      const { loadModelPhased } = await import('./useModelLoader');
      await loadModelPhased();

      const state = useModelStore.getState();
      expect(state.things.length).toBe(2);
      expect(state.loaded).toBe(true);
      expect(useUiStore.getState().loadingPhase).toBe('done');
    });

    it('skips loading when already loaded', async () => {
      useModelStore.setState({ loaded: true });

      const { loadModelPhased } = await import('./useModelLoader');
      await loadModelPhased();

      expect(thingApi.getSurfaceThings).not.toHaveBeenCalled();
      expect(useUiStore.getState().loadingPhase).toBe('done');
    });

    it('deduplicates concurrent calls (StrictMode double-fire)', async () => {
      vi.mocked(thingApi.getSurfaceThings).mockResolvedValue([surfaceThing]);
      vi.mocked(relationshipApi.getAll).mockResolvedValue([relationship]);
      vi.mocked(thingApi.getRemainingThings).mockResolvedValue([remainingThing]);

      const { loadModelPhased } = await import('./useModelLoader');

      // Simulate StrictMode: two calls without awaiting the first
      const p1 = loadModelPhased();
      const p2 = loadModelPhased();
      await Promise.all([p1, p2]);

      // Only one set of fetches should have been made
      expect(thingApi.getSurfaceThings).toHaveBeenCalledTimes(1);
      expect(useUiStore.getState().loadingPhase).toBe('done');
      expect(useModelStore.getState().loaded).toBe(true);
    });
  });

  it('dashboard stats are non-zero after phased loading', async () => {
    vi.mocked(thingApi.getSurfaceThings).mockResolvedValue([surfaceThing]);
    vi.mocked(relationshipApi.getAll).mockResolvedValue([relationship]);
    vi.mocked(thingApi.getRemainingThings).mockResolvedValue([remainingThing]);

    const { loadModelPhased } = await import('./useModelLoader');
    await loadModelPhased();

    const { things, relationships } = useModelStore.getState();
    expect(things.length).toBeGreaterThan(0);
    expect(relationships.length).toBeGreaterThan(0);
  });
});
