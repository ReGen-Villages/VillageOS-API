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
 * Reload things + relationships from Mycelium into the model store.
 * Exported so mutation handlers (delete thing, create relationship, save
 * property) can refresh after their action without going through the hook.
 * Single source of truth for the wire fetch.
 */
export async function reloadModelData(): Promise<void> {
  try {
    const [t, r] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
    useModelStore.getState().setThings(t);
    useModelStore.getState().setRelationships(r);
  } catch {
    toast.error('Failed to load model');
  }
}

/**
 * App-shell hook (Feature #5329): owns the lifecycle of model data so that
 * every authenticated page sees a populated `useModelStore` from the moment
 * they mount — not just GraphPage. Pulls the initial load and subscribes to
 * the Mycelium's SignalR events that keep the store live.
 *
 * Mount once in `AuthenticatedApp`. Returns nothing — it's effects-only.
 */
export function useModelData(): void {
  const { on } = useSse();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();

  // Initial load on mount
  useEffect(() => { reloadModelData(); }, []);

  // SignalR live updates
  useEffect(() => {
    const unsubs = [
      on('ThingCreated', () => reloadModelData()),
      on('ThingDeleted', () => reloadModelData()),
      on('RelationshipCreated', () => reloadModelData()),
      on('RelationshipDeleted', () => reloadModelData()),
      on('PropertyChanged', (...args: unknown[]) => {
        const thingId = args[0] as string;
        const propertyPath = args[1] as string | undefined;
        const newValue = args[2] as unknown;
        if (thingId && propertyPath !== undefined) {
          triggerFlashNode(thingId);
          // Only rebuild things array for properties that affect graph
          // rendering (geometry). Other property changes are detail-panel
          // concerns only — skip the O(n) array rebuild.
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
          // Only rebuild relationships array if this rel is currently
          // visible (selected node has it as outgoing/incoming).
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
