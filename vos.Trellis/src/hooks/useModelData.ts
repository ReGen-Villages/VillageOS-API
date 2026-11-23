import { useEffect } from 'react';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { NAVIGATION_AND_SETTINGS } from '../api/dashboardSubscription';
import { DASHBOARD_SPECIFICATION_PROPERTY } from '@tendril-ui/tendril';
import type { SubscriptionOpened, SubscriptionSelector } from '../types/subscription';
import { SUBSCRIPTION_CHANGING, SUBSCRIPTION_OPENED, resubscribe, useSse, useDefaultSubscription } from './useSse';
import { useFlashTimer } from './useFlashTimer';
import { toast } from '../components/common/toastStore';
import { isVisibleRelationship } from '../utils/propertyUpdates';
import i18n from '../i18n';
import { unwrapRelationship, unwrapThing } from '@tendril-ui/tendril';
import { readModelLoadProperties } from '../utils/guiSettings';
import { DISPLAY_SETTINGS_ROLE_ID } from '@tendril-ui/tendril';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';

/** A busy model sends hundreds of creations a second, and rebuilding the store for each would hold the main
 *  thread, so events are buffered and applied once per window. */
const FLUSH_DEBOUNCE_MILLISECONDS = 150;

/** What happened to one property in a flush window: it was given a value, or it was retracted. */
type PropertyChange = { deleted: false; value: unknown } | { deleted: true };

/**
 * Read before the model, because it decides what to ask for; it is one small request, and the settings
 * Thing is never narrowed, so it could not come with the load it configures. A model that says nothing, or
 * a failed read, loads everything, because narrowing on a guess would drop properties a page needs.
 */
async function declaredModelLoadProperties(): Promise<string[]> {
  try {
    const settings = await thingApi.getBoundBy(DISPLAY_SETTINGS_ROLE_ID);
    const declared = readModelLoadProperties(settings?.Properties ?? null);
    // The navigation reads every dashboard from this property, and a model narrowing its load is describing
    // its pages, not asking for fewer dashboards.
    if (declared.length && !declared.includes(DASHBOARD_SPECIFICATION_PROPERTY)) declared.push(DASHBOARD_SPECIFICATION_PROPERTY);
    return declared;
  } catch {
    return [];
  }
}

function holdsNarrowedSet(): boolean {
  const { loaded, holdsWholeModel } = useModelStore.getState();
  return loaded && !holdsWholeModel;
}

/** Cleared when the page asks rather than when its subscription opens, because until then a page that asked
 *  for the whole model would draw a narrower page's set as the model. */
function leaveANarrowedSet(next: SubscriptionSelector): void {
  if (next.all && holdsNarrowedSet()) useModelStore.getState().clear();
}

/** Exported so a change can refresh after itself without the hook; the only load that honours the
 *  properties the model says its pages use. `silent` withholds the failure message for a page already
 *  drawn, because that message stays until dismissed and a retrying loop would stack them. */
export async function reloadModelData(options?: { silent?: boolean }): Promise<void> {
  try {
    const declared = await declaredModelLoadProperties();
    const [t, r] = await Promise.all([thingApi.getAll(declared), relationshipApi.getAll()]);
    useModelStore.getState().setThings(t);
    useModelStore.getState().setRelationships(r);
    // Pages such as OperationsPage wait on this, and without it would show "Loading model…" for ever.
    useModelStore.getState().markLoaded({ wholeModel: true });
  } catch {
    if (!options?.silent) toast.error(i18n.t('modelPage.loadFailed'));
  }
}

/** A narrowed subscription's snapshot already is the load, so there is no second read, and a reconnect
 *  refills the page the same way rather than waiting for the stream to send what it missed. A whole-model
 *  subscription loads through the model read, the only one that honours the properties its pages use. */
function loadWhatOpened(opened: SubscriptionOpened): void {
  if (opened.covered) {
    useModelStore.getState().setThings(opened.covered.things);
    useModelStore.getState().setRelationships(opened.covered.relationships);
    useModelStore.getState().seedThingStates(opened.covered.thingStates);
    useModelStore.getState().markLoaded({ wholeModel: false });
    return;
  }
  // Only a load the person is waiting on reports its failure.
  const waitedOn = !useModelStore.getState().loaded;
  void reloadModelData({ silent: !waitedOn });
}

function entityId(data: unknown): string | undefined {
  return (data as { EntityId?: string } | undefined)?.EntityId;
}

/** In the shape the store holds, unwrapped as the snapshot is, with the states it held when the event was written. */
function carriedThing(data: unknown): { thing: VosThing; states: string[] } | undefined {
  const thing = (data as { Thing?: VosThing & { States: string[] } } | undefined)?.Thing;
  return thing ? { thing: unwrapThing(thing), states: thing.States } : undefined;
}

/** An event missing its state set is skipped rather than read as the entity having left every state. */
function stateChange(data: unknown): { id: string; states: string[]; exited: string[] } | undefined {
  const event = data as { entityId?: string; currentStates?: string[]; exitedStates?: string[] } | undefined;
  return event?.entityId && event.currentStates
    ? { id: event.entityId, states: event.currentStates, exited: event.exitedStates ?? [] }
    : undefined;
}

function statesTouched(change: { states: string[]; exited: string[] } | undefined): string[] {
  return change ? [...change.states, ...change.exited] : [];
}

function carriedRelationship(data: unknown): VosRelationship | undefined {
  const relationship = (data as { Relationship?: VosRelationship } | undefined)?.Relationship;
  return relationship ? unwrapRelationship(relationship) : undefined;
}

/**
 * Mounted once, above the routes. The load is whatever subscription the mounted pages declared, so a page
 * about a handful of Things is sent those. The shell declares the least any page needs, the dashboards the
 * navigation lists and the Thing holding the display settings, and a page reading across the model widens it.
 */
export function useModelData(): void {
  const { on } = useSse();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();
  useDefaultSubscription(NAVIGATION_AND_SETTINGS);

  useEffect(() => {
    // Property changes are keyed by entity and property name, because keying by entity kept only the last
    // change in a window. Within a window the last event for an entity wins: an arrival cancels a pending
    // removal, and a removal a pending arrival.
    const pending = {
      thingUpserts: new Map<string, VosThing>(),
      thingRemove: new Set<string>(),
      relationshipUpserts: new Map<string, VosRelationship>(),
      relationshipRemove: new Set<string>(),
      thingProperties: new Map<string, Map<string, PropertyChange>>(),
      relationshipProperties: new Map<string, Map<string, PropertyChange>>(),
      thingStates: new Map<string, string[]>(),
    };

    const recordProperty = (
      buffer: Map<string, Map<string, PropertyChange>>,
      entityId: string,
      propertyName: string,
      change: PropertyChange,
    ) => {
      const properties = buffer.get(entityId) ?? new Map<string, PropertyChange>();
      properties.set(propertyName, change);
      buffer.set(entityId, properties);
    };

    // Each property event is (entity id, property name, value); a retraction carries no value.
    const onThingProperty = (eventArguments: unknown[], change: PropertyChange) => {
      const [thingId, propertyPath] = eventArguments as [string, string | undefined];
      if (!thingId || propertyPath === undefined) return;
      triggerFlashNode(thingId);
      recordProperty(pending.thingProperties, thingId, propertyPath, change);
      schedule();
    };

    // Applied only while the relationship is on screen, opened itself or attached to the opened node.
    // Selecting a relationship clears the node selection, so asking about the node alone would miss it.
    const onRelationshipProperty = (eventArguments: unknown[], change: PropertyChange) => {
      const [relationshipId, propertyName] = eventArguments as [string, string | undefined];
      if (!relationshipId || propertyName === undefined) return;
      triggerFlashEdge(relationshipId);
      const { selectedNodeId, selectedEdgeId } = useUiStore.getState();
      if (!isVisibleRelationship(relationshipId, selectedNodeId, selectedEdgeId, useModelStore.getState().relationships)) return;
      recordProperty(pending.relationshipProperties, relationshipId, propertyName, change);
      schedule();
    };

    let timer: ReturnType<typeof setTimeout> | null = null;
    const schedule = () => { if (!timer) timer = setTimeout(flush, FLUSH_DEBOUNCE_MILLISECONDS); };

    function flush(): void {
      timer = null;
      const thingUpserts = [...pending.thingUpserts.values()]; pending.thingUpserts.clear();
      const relationshipUpserts = [...pending.relationshipUpserts.values()]; pending.relationshipUpserts.clear();
      const thingRemovals = [...pending.thingRemove]; pending.thingRemove.clear();
      const relationshipRemovals = [...pending.relationshipRemove]; pending.relationshipRemove.clear();
      const thingPropertyUpdates: { id: string; path: string; value: unknown }[] = [];
      const thingPropertyRemovals: { id: string; path: string }[] = [];
      for (const [id, properties] of pending.thingProperties)
        for (const [path, change] of properties)
          if (change.deleted) thingPropertyRemovals.push({ id, path });
          else thingPropertyUpdates.push({ id, path, value: change.value });
      pending.thingProperties.clear();

      const relationshipPropertyUpdates: { id: string; name: string; value: unknown }[] = [];
      const relationshipPropertyRemovals: { id: string; name: string }[] = [];
      for (const [id, properties] of pending.relationshipProperties)
        for (const [name, change] of properties)
          if (change.deleted) relationshipPropertyRemovals.push({ id, name });
          else relationshipPropertyUpdates.push({ id, name, value: change.value });
      pending.relationshipProperties.clear();

      const thingStateUpdates = [...pending.thingStates].map(([id, states]) => ({ id, states }));
      pending.thingStates.clear();

      useModelStore.getState().applyBatch({
        thingUpserts,
        thingRemovals,
        relationshipUpserts,
        relationshipRemovals,
        thingPropertyUpdates,
        relationshipPropertyUpdates,
        thingPropertyRemovals,
        relationshipPropertyRemovals,
        thingStateUpdates,
      });
    }

    const thingArrived = (data: unknown) => {
      const carried = carriedThing(data);
      if (!carried) return;
      pending.thingRemove.delete(carried.thing.Id);
      pending.thingUpserts.set(carried.thing.Id, carried.thing);
      if (holdsNarrowedSet()) pending.thingStates.set(carried.thing.Id, carried.states);
      schedule();
    };
    const thingGone = (data: unknown) => {
      const id = entityId(data);
      if (id) { pending.thingUpserts.delete(id); pending.thingRemove.add(id); schedule(); }
    };
    const relationshipArrived = (data: unknown) => {
      const relationship = carriedRelationship(data);
      if (relationship) {
        pending.relationshipRemove.delete(relationship.Id);
        pending.relationshipUpserts.set(relationship.Id, relationship);
        schedule();
      }
    };
    const relationshipGone = (data: unknown) => {
      const id = entityId(data);
      if (id) { pending.relationshipUpserts.delete(id); pending.relationshipRemove.add(id); schedule(); }
    };

    const unsubs = [
      on('ThingCreated', thingArrived),
      on('ThingEntered', thingArrived),
      on('ThingDeleted', thingGone),
      on('ThingLeft', thingGone),
      on('RelationshipCreated', relationshipArrived),
      on('RelationshipEntered', relationshipArrived),
      on('RelationshipDeleted', relationshipGone),
      on('RelationshipLeft', relationshipGone),
      // Every property change is kept, not only those the graph draws, because the operations dashboard reads
      // live values from the store.
      on('PropertyChanged', (...eventArguments) => onThingProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('PropertyObserved', (...eventArguments) => onThingProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('PropertyDeleted', (...eventArguments) => onThingProperty(eventArguments, { deleted: true })),
      on('RelationshipPropertyChanged', (...eventArguments) => onRelationshipProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('RelationshipPropertyDeleted', (...eventArguments) => onRelationshipProperty(eventArguments, { deleted: true })),
      // Every open: the first, a reconnect, and a page changing what the subscription covers.
      on(SUBSCRIPTION_CHANGING, (selector) => leaveANarrowedSet(selector as SubscriptionSelector)),
      on(SUBSCRIPTION_OPENED, (data) => loadWhatOpened(data as SubscriptionOpened)),
      // A replaced model is not the one the subscription resolved against, so it is asked for
      // again rather than reconciled — which also re-answers with the new model's snapshot.
      on('ModelChanged', () => resubscribe()),
      on('ModelCleared', () => useModelStore.getState().clear()),
      // The narrowed page's set is kept only while the store holds one: the events stream carries
      // every Thing's state changes, and a page reading across the whole model draws none of them,
      // so keeping them there would grow with the model for no reader.
      on('StatesChanged', (data) => {
        const change = stateChange(data);
        useUiStore.getState().statesMoved(statesTouched(change));
        if (holdsNarrowedSet() && change) { pending.thingStates.set(change.id, change.states); schedule(); }
      }),
      on('RelationshipStatesChanged', (data) => useUiStore.getState().statesMoved(statesTouched(stateChange(data)))),
    ];
    return () => {
      if (timer) clearTimeout(timer);
      unsubs.forEach((u) => u());
    };
  }, [on, triggerFlashNode, triggerFlashEdge]);
}
