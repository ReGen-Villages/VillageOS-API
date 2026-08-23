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
import { GUI_SETTINGS_TYPE_NAME, readModelLoadProperties } from '../utils/guiSettings';

/** How long to wait before a single hydrate retry (Bug #5940). */
const HYDRATE_RETRY_MS = 400;

/** Coalesce a burst of SSE structural events into one store write. A high-throughput
 *  sim emits hundreds of ThingCreated/RelationshipCreated per second; applying each as
 *  its own O(N) store rebuild saturates the main thread and makes Trellis degrade as the
 *  model grows. We buffer events and flush once per window instead. */
const FLUSH_DEBOUNCE_MS = 150;

/** Max hydrate fetches in flight per flush — bounds the request fan-out on a big burst. */
const HYDRATE_CONCURRENCY = 8;

/** What happened to one property in a flush window: it was given a value, or it was retracted. */
type PropertyChange = { deleted: false; value: unknown } | { deleted: true };

/**
 * Single source of truth for the model fetch. Exported so mutation handlers can
 * refresh after their action without going through the hook.
 *
 * Pass `{ silent: true }` for background reconciles (e.g. the SSE reconnect
 * recovery) so a transient failure does not raise a toast — error toasts do not
 * auto-dismiss, so a background loop would otherwise stack un-dismissable toasts
 * (Bug #5940). User-initiated loads (mount, mutations, ModelChanged) stay loud.
 */
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

export async function reloadModelData(opts?: { silent?: boolean }): Promise<void> {
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
    if (!opts?.silent) toast.error('Failed to load model');
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

const delay = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

// Structural create events carry only an id — the object's properties are
// deliberately not streamed — so we hydrate the new objects and upsert them
// rather than refetching the whole model. Each fetch is retried once on failure
// (Bug #5940): a transient error would otherwise drop the object from the store
// until the next ModelChanged. A genuine create/delete race 404s on the retry
// too and is correctly abandoned (the delete event removes it).
async function hydrateMany<T>(ids: string[], fetchOne: (id: string) => Promise<T>): Promise<T[]> {
  const out: T[] = [];
  let cursor = 0;
  async function worker(): Promise<void> {
    while (cursor < ids.length) {
      const id = ids[cursor++];
      try {
        out.push(await fetchOne(id));
      } catch {
        try {
          await delay(HYDRATE_RETRY_MS);
          out.push(await fetchOne(id));
        } catch { /* gone or still failing — reconciled by reconnect/ModelChanged */ }
      }
    }
  }
  const workers = Array.from({ length: Math.min(HYDRATE_CONCURRENCY, ids.length) }, worker);
  await Promise.all(workers);
  return out;
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
    const pending = {
      thingHydrate: new Set<string>(),
      thingRemove: new Set<string>(),
      relHydrate: new Set<string>(),
      relRemove: new Set<string>(),
      thingProps: new Map<string, Map<string, PropertyChange>>(),
      relProps: new Map<string, Map<string, PropertyChange>>(),
    };
    const isEmpty = () =>
      pending.thingHydrate.size === 0 && pending.thingRemove.size === 0 &&
      pending.relHydrate.size === 0 && pending.relRemove.size === 0 &&
      pending.thingProps.size === 0 && pending.relProps.size === 0;

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
    const onThingProperty = (args: unknown[], change: PropertyChange) => {
      const [thingId, propertyPath] = args as [string, string | undefined];
      if (!thingId || propertyPath === undefined) return;
      triggerFlashNode(thingId);
      recordProperty(pending.thingProps, thingId, propertyPath, change);
      schedule();
    };

    // Applied only while the relationship is on screen — as the opened edge, or hanging off the
    // opened node. Asking about the node alone dropped every change to the edge whose own panel was
    // in front of the user, because selecting an edge clears the node selection.
    const onRelationshipProperty = (args: unknown[], change: PropertyChange) => {
      const [relId, propertyName] = args as [string, string | undefined];
      if (!relId || propertyName === undefined) return;
      triggerFlashEdge(relId);
      const { selectedNodeId, selectedEdgeId } = useUiStore.getState();
      if (!isVisibleRelationship(relId, selectedNodeId, selectedEdgeId, useModelStore.getState().relationships)) return;
      recordProperty(pending.relProps, relId, propertyName, change);
      schedule();
    };

    let timer: ReturnType<typeof setTimeout> | null = null;
    let flushing = false;
    const schedule = () => { if (!timer && !flushing) timer = setTimeout(() => void flush(), FLUSH_DEBOUNCE_MS); };

    async function flush(): Promise<void> {
      timer = null;
      flushing = true;
      // Snapshot and clear the buffers up front so events arriving during the async
      // hydrate below land in the next window rather than being dropped.
      const thingIds = [...pending.thingHydrate]; pending.thingHydrate.clear();
      const relIds = [...pending.relHydrate]; pending.relHydrate.clear();
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

      const [thingUpserts, relationshipUpserts] = await Promise.all([
        hydrateMany(thingIds, (id) => thingApi.get(id)),
        hydrateMany(relIds, (id) => relationshipApi.get(id)),
      ]);

      useModelStore.getState().applyBatch({
        thingUpserts,
        thingRemovals,
        relationshipUpserts,
        relationshipRemovals,
        thingPropertyUpdates,
        relationshipPropertyUpdates,
        thingPropertyRemovals,
        relationshipPropertyRemovals,
      });

      flushing = false;
      if (!isEmpty()) schedule(); // events arrived mid-flush — drain them next window
    }

    const unsubs = [
      on('ThingCreated', (data) => {
        const id = entityId(data);
        if (id) { pending.thingRemove.delete(id); pending.thingHydrate.add(id); schedule(); }
      }),
      on('ThingDeleted', (data) => {
        const id = entityId(data);
        if (id) { pending.thingHydrate.delete(id); pending.thingRemove.add(id); schedule(); }
      }),
      on('RelationshipCreated', (data) => {
        const id = entityId(data);
        if (id) { pending.relRemove.delete(id); pending.relHydrate.add(id); schedule(); }
      }),
      on('RelationshipDeleted', (data) => {
        const id = entityId(data);
        if (id) { pending.relHydrate.delete(id); pending.relRemove.add(id); schedule(); }
      }),
      // Every property update lands in the store, not just graph-rendering ones. The Operations
      // dashboard reads live business properties (on-hand, reorder point, KPIs) straight from the
      // store, so dropping their updates left it showing stale or blank cells for anything changed
      // after the last full load. The debounced applyBatch coalesces the high rate into one write
      // per window.
      on('PropertyChanged', (...args) => onThingProperty(args, { deleted: false, value: args[2] })),
      on('PropertyDeleted', (...args) => onThingProperty(args, { deleted: true })),
      on('RelationshipPropertyChanged', (...args) => onRelationshipProperty(args, { deleted: false, value: args[2] })),
      on('RelationshipPropertyDeleted', (...args) => onRelationshipProperty(args, { deleted: true })),
      // Every open: the first, a reconnect, and a page changing what the subscription covers.
      on(SUBSCRIPTION_OPENED, (data) => loadWhatOpened(data as SubscriptionOpened)),
      // A replaced model is not the one the subscription resolved against, so it is asked for
      // again rather than reconciled — which also re-answers with the new model's snapshot.
      on('ModelChanged', () => resubscribe()),
      on('ModelCleared', () => useModelStore.getState().clear()),
      on('StatesChanged', () => useUiStore.getState().bumpStatesVersion()),
    ];
    return () => {
      if (timer) clearTimeout(timer);
      unsubs.forEach((u) => u());
    };
  }, [on, triggerFlashNode, triggerFlashEdge]);
}
