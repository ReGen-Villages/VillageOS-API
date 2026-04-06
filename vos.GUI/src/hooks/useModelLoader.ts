import { useEffect, useCallback } from 'react';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';

/**
 * Module-level guard — survives React StrictMode mount/unmount/remount
 * cycles so the phased load only fires once.
 */
let loadInFlight: Promise<void> | null = null;

/**
 * Standalone phased load — can be called from the hook or from tests.
 * Runs all three phases sequentially, falling back to a full load if the
 * phase endpoints are unavailable.
 *
 * Safe to call multiple times (idempotent): returns immediately if already
 * loaded, and deduplicates concurrent calls via a module-level promise.
 */
export async function loadModelPhased(): Promise<void> {
  if (useModelStore.getState().loaded) {
    useUiStore.getState().setLoadingPhase('done');
    return;
  }

  // Deduplicate: if a load is already in flight, join it
  if (loadInFlight) return loadInFlight;

  loadInFlight = (async () => {
    const { setThings, setRelationships, markLoaded } = useModelStore.getState();
    const { setLoadingPhase } = useUiStore.getState();

    try {
      // Phase 1: surface things (with lat/lng + footprint properties) + relationships
      setLoadingPhase('surface');
      const [surfaceThings, r] = await Promise.all([
        thingApi.getSurfaceThings(),
        relationshipApi.getAll(),
      ]);
      setThings(surfaceThings);
      setRelationships(r);

      // Phase 2: remaining (non-surface) things
      setLoadingPhase('remaining');
      const remainingThings = await thingApi.getRemainingThings();
      const allThings = [...surfaceThings, ...remainingThings];
      setThings(allThings);

      // No Phase 3 geometry fetch — footprints and centroids are pre-computed
      // properties included in Phases 1+2. Full geometry blobs are only
      // fetched on-demand for 3D detail views.

      setLoadingPhase('done');
      markLoaded();
    } catch {
      // Fallback: phase 1 or 2 endpoints don't exist (small seed without
      // surface classification). Fall back to full load.
      useUiStore.getState().setLoadingPhase('surface');
      const { setThings: st, setRelationships: sr } = useModelStore.getState();
      const [t, rel] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
      st(t);
      sr(rel);
      useUiStore.getState().setLoadingPhase('done');
      useModelStore.getState().markLoaded();
    } finally {
      loadInFlight = null;
    }
  })();

  return loadInFlight;
}

/** Reset the in-flight guard (for tests). */
export function _resetLoadGuard() {
  loadInFlight = null;
}

/**
 * Runs two-phase model loading once after authentication, regardless of
 * which page the user lands on.  Phases:
 *   1. Surface things (with lat/lng + footprint properties) + relationships
 *   2. Remaining (non-surface) things
 *
 * Falls back to a full load if the phase endpoints are unavailable.
 * Full geometry blobs are fetched on-demand for 3D detail views only.
 */
export function useModelLoader() {
  const loadFull = useCallback(async () => {
    const { setThings, setRelationships } = useModelStore.getState();
    const [t, r] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
    setThings(t);
    setRelationships(r);
  }, []);

  useEffect(() => {
    loadModelPhased();
  }, []);

  return { loadFull };
}
