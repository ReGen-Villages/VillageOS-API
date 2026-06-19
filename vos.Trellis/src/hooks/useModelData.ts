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
  } catch {
    toast.error('Failed to load model');
  }
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
