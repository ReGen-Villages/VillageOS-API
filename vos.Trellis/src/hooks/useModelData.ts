import { useEffect } from 'react';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { useSse } from './useSse';
import { useFlashTimer } from './useFlashTimer';
import { toast } from '../components/common/Toast';
import { isGraphAffectingProperty, applyThingPropertyUpdate, applyRelationshipPropertyUpdate, isVisibleRelationship } from '../utils/propertyUpdates';

/**
 * Single source of truth for the model fetch. Exported so mutation handlers can
 * refresh after their action without going through the hook.
 */
export async function reloadModelData(): Promise<void> {
  try {
    const [t, r] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
    useModelStore.getState().setThings(t);
    useModelStore.getState().setRelationships(r);
    // Flip the gate that pages (e.g. OperationsPage) block rendering on. Without
    // this the Operations page sits on "Loading model…" forever (Bug #5930).
    useModelStore.getState().markLoaded();
  } catch {
    toast.error('Failed to load model');
  }
}

/** The entity id carried by a structural change event (ThingCreated, etc.). */
function entityId(data: unknown): string | undefined {
  return (data as { EntityId?: string } | undefined)?.EntityId;
}

// Structural create events carry only an id — the object's properties are
// deliberately not streamed — so we hydrate the single new object and upsert it
// rather than refetching the whole model. A failed fetch (e.g. the create raced
// with a delete) is ignored: a later ModelChanged/reload reconciles the store.
async function hydrateThing(id: string | undefined): Promise<void> {
  if (!id) return;
  try {
    useModelStore.getState().upsertThing(await thingApi.get(id));
  } catch { /* transient or already deleted — leave the store as-is */ }
}

async function hydrateRelationship(id: string | undefined): Promise<void> {
  if (!id) return;
  try {
    useModelStore.getState().upsertRelationship(await relationshipApi.get(id));
  } catch { /* transient or already deleted — leave the store as-is */ }
}

/**
 * App-shell hook: owns model-data lifecycle so every authenticated page sees a
 * populated useModelStore from mount. Mount once in AuthenticatedApp.
 */
export function useModelData(): void {
  const { on } = useSse();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();

  useEffect(() => { reloadModelData(); }, []);

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
