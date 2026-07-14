import { useEffect, useRef } from 'react';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { useSse } from './useSse';
import { useFlashTimer } from './useFlashTimer';
import { toast } from '../components/common/Toast';
import { isGraphAffectingProperty, applyThingPropertyUpdate, applyRelationshipPropertyUpdate, isVisibleRelationship } from '../utils/propertyUpdates';

/** How long to wait before a single hydrate retry (Bug #5940). */
const HYDRATE_RETRY_MS = 400;

/**
 * Single source of truth for the model fetch. Exported so mutation handlers can
 * refresh after their action without going through the hook.
 *
 * Pass `{ silent: true }` for background reconciles (e.g. the SSE reconnect
 * recovery) so a transient failure does not raise a toast — error toasts do not
 * auto-dismiss, so a background loop would otherwise stack un-dismissable toasts
 * (Bug #5940). User-initiated loads (mount, mutations, ModelChanged) stay loud.
 */
export async function reloadModelData(opts?: { silent?: boolean }): Promise<void> {
  try {
    const [t, r] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
    useModelStore.getState().setThings(t);
    useModelStore.getState().setRelationships(r);
    // Flip the gate that pages (e.g. OperationsPage) block rendering on. Without
    // this the Operations page sits on "Loading model…" forever (Bug #5930).
    useModelStore.getState().markLoaded();
  } catch {
    if (!opts?.silent) toast.error('Failed to load model');
  }
}

/** The entity id carried by a structural change event (ThingCreated, etc.). */
function entityId(data: unknown): string | undefined {
  return (data as { EntityId?: string } | undefined)?.EntityId;
}

const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

// Structural create events carry only an id — the object's properties are
// deliberately not streamed — so we hydrate the single new object and upsert it
// rather than refetching the whole model. The fetch is retried once on failure
// (Bug #5940): a transient error would otherwise drop the object from the store
// until the next ModelChanged. A genuine create/delete race 404s on the retry
// too and is correctly abandoned (the delete event removes it).
async function hydrateThing(id: string | undefined): Promise<void> {
  if (!id) return;
  try {
    useModelStore.getState().upsertThing(await thingApi.get(id));
  } catch {
    try {
      await delay(HYDRATE_RETRY_MS);
      useModelStore.getState().upsertThing(await thingApi.get(id));
    } catch { /* gone or still failing — reconciled by reconnect/ModelChanged */ }
  }
}

async function hydrateRelationship(id: string | undefined): Promise<void> {
  if (!id) return;
  try {
    useModelStore.getState().upsertRelationship(await relationshipApi.get(id));
  } catch {
    try {
      await delay(HYDRATE_RETRY_MS);
      useModelStore.getState().upsertRelationship(await relationshipApi.get(id));
    } catch { /* gone or still failing — reconciled by reconnect/ModelChanged */ }
  }
}

/**
 * App-shell hook: owns model-data lifecycle so every authenticated page sees a
 * populated useModelStore from mount. Mount once in AuthenticatedApp.
 */
export function useModelData(): void {
  const { on, connected } = useSse();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();

  useEffect(() => { reloadModelData(); }, []);

  // Reconcile once when the SSE stream RECOVERS (Bug #5940). While disconnected the
  // incremental handlers miss structural events, so on reconnect we resync the whole
  // store to recover the gap. `hasConnected` gates out the first connect (the mount
  // effect already loads) so only genuine reconnects trigger a reconcile. Silent: a
  // background refresh must not raise an un-dismissable "Failed to load model" toast.
  const hasConnected = useRef(false);
  useEffect(() => {
    if (!connected) return;
    if (hasConnected.current) reloadModelData({ silent: true });
    hasConnected.current = true;
  }, [connected]);

  useEffect(() => {
    const unsubs = [
      on('ThingCreated', (data) => hydrateThing(entityId(data))),
      on('ThingDeleted', (data) => useModelStore.getState().removeThing(entityId(data) ?? '')),
      on('RelationshipCreated', (data) => hydrateRelationship(entityId(data))),
      on('RelationshipDeleted', (data) => useModelStore.getState().removeRelationship(entityId(data) ?? '')),
      on('PropertyChanged', (...args: unknown[]) => {
        const thingId = args[0] as string;
        const propertyPath = args[1] as string | undefined;
        const newValue = args[2] as unknown;
        if (thingId && propertyPath !== undefined) {
          triggerFlashNode(thingId);
          // Skip the O(n) rebuild unless the property affects graph rendering;
          // other changes are detail-panel concerns only.
          if (isGraphAffectingProperty(propertyPath)) {
            useModelStore.getState().updateThings((prev) => prev.map((t) =>
              t.Id === thingId ? applyThingPropertyUpdate(t, propertyPath, newValue) : t,
            ));
          }
        }
      }),
      on('RelationshipPropertyChanged', (...args: unknown[]) => {
        const relId = args[0] as string;
        const propertyName = args[1] as string | undefined;
        const newValue = args[2] as unknown;
        if (relId && propertyName !== undefined) {
          triggerFlashEdge(relId);
          // Only rebuild if this rel is currently visible on the selected node.
          const selNode = useUiStore.getState().selectedNodeId;
          const rels = useModelStore.getState().relationships;
          if (isVisibleRelationship(relId, selNode, rels)) {
            useModelStore.getState().updateRelationships((prev) => prev.map((r) =>
              r.Id === relId ? applyRelationshipPropertyUpdate(r, propertyName, newValue) : r,
            ));
          }
        }
      }),
      on('ModelChanged', () => reloadModelData()),
      on('ModelCleared', () => useModelStore.getState().clear()),
      on('StatesChanged', () => useUiStore.getState().bumpStatesVersion()),
    ];
    return () => unsubs.forEach((u) => u());
  }, [on, triggerFlashNode, triggerFlashEdge]);
}
