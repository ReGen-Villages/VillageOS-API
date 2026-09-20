import { useEffect } from 'react';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { NAVIGATION_AND_SETTINGS } from '../api/dashboardSubscription';
import { DASHBOARD_SPEC_PROPERTY } from '../types/dashboard';
import type { SubscriptionOpened } from '../types/subscription';
import { SUBSCRIPTION_OPENED, resubscribe, useSse, useDefaultSubscription } from './useSse';
import { useFlashTimer } from './useFlashTimer';
import { toast } from '../components/common/toastStore';
import { isVisibleRelationship } from '../utils/propertyUpdates';
import { unwrapRelationship, unwrapThing } from '../utils/propertyMapper';
import { GUI_SETTINGS_TYPE_NAME, readModelLoadProperties } from '../utils/guiSettings';
import type { VosRelationship, VosThing } from '../types/vos';

/** Coalesce a burst of SSE structural events into one store write. A high-throughput
 *  sim emits hundreds of ThingCreated/RelationshipCreated per second; applying each as
 *  its own O(N) store rebuild saturates the main thread and makes Trellis degrade as the
 *  model grows. We buffer events and flush once per window instead. */
const FLUSH_DEBOUNCE_MS = 150;

/** What happened to one property in a flush window: it was given a value, or it was retracted. */
type PropertyChange = { deleted: false; value: unknown } | { deleted: true };

/**
 * The properties this model says its pages are drawn with, or none when it says nothing.
 *
 * Read before the model itself because it decides what to ask for. It costs one small request against
 * a route that already existed, next to a load that is megabytes — and the platform never narrows the
 * settings Thing, so this could not have ridden in the load it configures.
 *
 * A model that says nothing, or a read that fails, loads everything. Narrowing on a guess would strip
 * properties a page needs; loading everything is only slower.
 */
async function declaredModelLoadProperties(): Promise<string[]> {
  try {
    const settings = await thingApi.getByName(GUI_SETTINGS_TYPE_NAME);
    const declared = readModelLoadProperties(settings?.Properties ?? null);
    // The navigation lists a model's dashboards on every page, and it reads each one out of this
    // property. A model that narrows its load without naming it is not asking for fewer dashboards
    // in the navigation — it is describing what its pages are drawn with.
    if (declared.length && !declared.includes(DASHBOARD_SPEC_PROPERTY)) declared.push(DASHBOARD_SPEC_PROPERTY);
    return declared;
  } catch {
    return [];
  }
}

/**
 * Whether the store currently holds one page's own set of Things rather than the whole model.
 *
 * A page that asked for the whole model must not be shown a narrower page's set as though it were
 * the model, so the store is emptied before its load rather than left showing someone else's
 * answer. Held here rather than in the store because it describes the load, not the model.
 */
let holdsNarrowedSet = false;

/**
 * The whole-model read. Exported so a mutation handler can refresh after its own action without
 * going through the hook, and the only load that honours the properties the model says its pages
 * are drawn with.
 *
 * `silent` withholds the failure toast, for a read behind a page that is already drawn: an error
 * toast does not auto-dismiss, so a retrying loop would stack un-dismissable ones (Bug #5940).
 */
export async function reloadModelData(options?: { silent?: boolean }): Promise<void> {
  try {
    const declared = await declaredModelLoadProperties();
    const [t, r] = await Promise.all([thingApi.getAll(declared), relationshipApi.getAll()]);
    useModelStore.getState().setThings(t);
    useModelStore.getState().setRelationships(r);
    holdsNarrowedSet = false;
    // Flip the gate that pages (e.g. OperationsPage) block rendering on. Without
    // this the Operations page sits on "Loading model…" forever (Bug #5930).
    useModelStore.getState().markLoaded();
  } catch {
    if (!options?.silent) toast.error('Failed to load model');
  }
}

/**
 * The load a newly opened subscription either is or asks for.
 *
 * A narrowed subscription already answered with the Things the page is about, so its snapshot is
 * the load — no second read, and a reconnect refills the page the same way rather than waiting for
 * the stream to re-deliver what it missed. A whole-model subscription is loaded through the model
 * read instead, which is the only one that honours the properties the model says its pages are
 * drawn with.
 */
function loadWhatOpened(opened: SubscriptionOpened): void {
  if (opened.covered) {
    useModelStore.getState().setThings(opened.covered.things);
    useModelStore.getState().setRelationships(opened.covered.relationships);
    useModelStore.getState().seedThingStates(opened.covered.thingStates);
    holdsNarrowedSet = true;
    useModelStore.getState().markLoaded();
    return;
  }
  if (holdsNarrowedSet) useModelStore.getState().clear();
  // A load the user is waiting on says when it failed; a refresh behind an already-drawn page does
  // not, because an error toast does not auto-dismiss and a retrying loop would stack them (#5940).
  const waitedOn = !useModelStore.getState().loaded;
  void reloadModelData({ silent: !waitedOn });
}

/** The entity id carried by a structural change event (ThingCreated, etc.). */
function entityId(data: unknown): string | undefined {
  return (data as { EntityId?: string } | undefined)?.EntityId;
}

/** The Thing a creation or entry carries, in the shape the store holds — the snapshot's shape,
 *  unwrapped the same way — with the states it held when the event was written. An event carrying
 *  none has nothing to apply. */
function carriedThing(data: unknown): { thing: VosThing; states: string[] } | undefined {
  const thing = (data as { Thing?: VosThing & { States: string[] } } | undefined)?.Thing;
  return thing ? { thing: unwrapThing(thing), states: thing.States } : undefined;
}

/** A derived-state event names its entity and the whole set of states it now holds — never a delta.
 *  One missing its state set is skipped rather than read as the entity having left every state. */
function stateChange(data: unknown): { id: string; states: string[] } | undefined {
  const event = data as { entityId?: string; currentStates?: string[] } | undefined;
  return event?.entityId && event.currentStates ? { id: event.entityId, states: event.currentStates } : undefined;
}

function carriedRelationship(data: unknown): VosRelationship | undefined {
  const relationship = (data as { Relationship?: VosRelationship } | undefined)?.Relationship;
  return relationship ? unwrapRelationship(relationship) : undefined;
}

/**
 * App-shell hook: owns model-data lifecycle so every authenticated page sees a
 * populated useModelStore from mount. Mount once in AuthenticatedApp.
 *
 * The load comes from whatever subscription the mounted pages declared, so a page that is about a
 * handful of Things is sent those and no more. The shell's own declaration is the least any page
 * needs — the dashboards the navigation lists and the Thing the model states its display settings
 * on — and a page reading across the model widens it by declaring so.
 */
export function useModelData(): void {
  const { on } = useSse();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();
  useDefaultSubscription(NAVIGATION_AND_SETTINGS);

  useEffect(() => {
    // Buffered live updates: SSE events accumulate here and flush together, so a burst
    // of structural changes becomes one store write instead of one O(N) rebuild each.
    // Property buffers are keyed by entity AND property name: keying by entity alone kept only the
    // last change in a window, which the full model reload on save used to hide (#6143).
    // Of one window's events for an entity the last word wins: an arrival cancels a pending
    // removal and a removal cancels a pending arrival.
    const pending = {
      thingUpserts: new Map<string, VosThing>(),
      thingRemove: new Set<string>(),
      relUpserts: new Map<string, VosRelationship>(),
      relRemove: new Set<string>(),
      thingProps: new Map<string, Map<string, PropertyChange>>(),
      relProps: new Map<string, Map<string, PropertyChange>>(),
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
      recordProperty(pending.thingProps, thingId, propertyPath, change);
      schedule();
    };

    // Applied only while the relationship is on screen — as the opened edge, or hanging off the
    // opened node. Asking about the node alone dropped every change to the edge whose own panel was
    // in front of the user, because selecting an edge clears the node selection.
    const onRelationshipProperty = (eventArguments: unknown[], change: PropertyChange) => {
      const [relId, propertyName] = eventArguments as [string, string | undefined];
      if (!relId || propertyName === undefined) return;
      triggerFlashEdge(relId);
      const { selectedNodeId, selectedEdgeId } = useUiStore.getState();
      if (!isVisibleRelationship(relId, selectedNodeId, selectedEdgeId, useModelStore.getState().relationships)) return;
      recordProperty(pending.relProps, relId, propertyName, change);
      schedule();
    };

    let timer: ReturnType<typeof setTimeout> | null = null;
    const schedule = () => { if (!timer) timer = setTimeout(flush, FLUSH_DEBOUNCE_MS); };

    function flush(): void {
      timer = null;
      const thingUpserts = [...pending.thingUpserts.values()]; pending.thingUpserts.clear();
      const relationshipUpserts = [...pending.relUpserts.values()]; pending.relUpserts.clear();
      const thingRemovals = [...pending.thingRemove]; pending.thingRemove.clear();
      const relationshipRemovals = [...pending.relRemove]; pending.relRemove.clear();
      const thingPropertyUpdates: { id: string; path: string; value: unknown }[] = [];
      const thingPropertyRemovals: { id: string; path: string }[] = [];
      for (const [id, properties] of pending.thingProps)
        for (const [path, change] of properties)
          if (change.deleted) thingPropertyRemovals.push({ id, path });
          else thingPropertyUpdates.push({ id, path, value: change.value });
      pending.thingProps.clear();

      const relationshipPropertyUpdates: { id: string; name: string; value: unknown }[] = [];
      const relationshipPropertyRemovals: { id: string; name: string }[] = [];
      for (const [id, properties] of pending.relProps)
        for (const [name, change] of properties)
          if (change.deleted) relationshipPropertyRemovals.push({ id, name });
          else relationshipPropertyUpdates.push({ id, name, value: change.value });
      pending.relProps.clear();

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
      if (holdsNarrowedSet) pending.thingStates.set(carried.thing.Id, carried.states);
      schedule();
    };
    const thingGone = (data: unknown) => {
      const id = entityId(data);
      if (id) { pending.thingUpserts.delete(id); pending.thingRemove.add(id); schedule(); }
    };
    const relationshipArrived = (data: unknown) => {
      const relationship = carriedRelationship(data);
      if (relationship) {
        pending.relRemove.delete(relationship.Id);
        pending.relUpserts.set(relationship.Id, relationship);
        schedule();
      }
    };
    const relationshipGone = (data: unknown) => {
      const id = entityId(data);
      if (id) { pending.relUpserts.delete(id); pending.relRemove.add(id); schedule(); }
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
      // Every property update lands in the store, not just graph-rendering ones. The Operations
      // dashboard reads live business properties (on-hand, reorder point, KPIs) straight from the
      // store, so dropping their updates left it showing stale or blank cells for anything changed
      // after the last full load. The debounced applyBatch coalesces the high rate into one write
      // per window.
      on('PropertyChanged', (...eventArguments) => onThingProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('PropertyObserved', (...eventArguments) => onThingProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('PropertyDeleted', (...eventArguments) => onThingProperty(eventArguments, { deleted: true })),
      on('RelationshipPropertyChanged', (...eventArguments) => onRelationshipProperty(eventArguments, { deleted: false, value: eventArguments[2] })),
      on('RelationshipPropertyDeleted', (...eventArguments) => onRelationshipProperty(eventArguments, { deleted: true })),
      // Every open: the first, a reconnect, and a page changing what the subscription covers.
      on(SUBSCRIPTION_OPENED, (data) => loadWhatOpened(data as SubscriptionOpened)),
      // A replaced model is not the one the subscription resolved against, so it is asked for
      // again rather than reconciled — which also re-answers with the new model's snapshot.
      on('ModelChanged', () => resubscribe()),
      on('ModelCleared', () => useModelStore.getState().clear()),
      // The narrowed page's set is kept only while the store holds one: the events stream carries
      // every Thing's state changes, and a page reading across the whole model draws none of them,
      // so keeping them there would grow with the model for no reader. The stream says what is held
      // after the change and never which state was left, so a figure counting a state nobody
      // entered waits for the page's cadence to see it empty.
      on('StatesChanged', (data) => {
        const change = stateChange(data);
        useUiStore.getState().statesMoved(change?.states ?? []);
        if (holdsNarrowedSet && change) { pending.thingStates.set(change.id, change.states); schedule(); }
      }),
      on('RelationshipStatesChanged', (data) => useUiStore.getState().statesMoved(stateChange(data)?.states ?? [])),
    ];
    return () => {
      if (timer) clearTimeout(timer);
      unsubs.forEach((u) => u());
    };
  }, [on, triggerFlashNode, triggerFlashEdge]);
}
