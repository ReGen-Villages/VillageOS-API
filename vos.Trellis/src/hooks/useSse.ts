import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';
import { unwrapThing, unwrapRelationship } from '@tendril-ui/tendril';
import { WHOLE_MODEL, type CoveredObjects, type SubscriptionOpened, type SubscriptionSelector } from '../types/subscription';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import type { ConnectionState } from '../types/connection';
import { HEARTBEAT_AS_EVENT, watchForSilence, type SilenceWatch } from './streamSilence';

// Two streams, the subscription the page declared and the system events, feed one set of handlers.

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

const KNOWN_EVENTS = [
  'ModelChanged', 'ModelCleared',
  'ThingCreated', 'ThingDeleted', 'RelationshipCreated', 'RelationshipDeleted',
  'ThingEntered', 'ThingLeft', 'RelationshipEntered', 'RelationshipLeft',
  'PropertyChanged', 'PropertyDeleted', 'RelationshipPropertyChanged', 'RelationshipPropertyDeleted',
  'PropertyObserved',
  'ServiceHealthChanged', 'DaemonStatusChanged', 'EndpointServiceRequestCompleted', 'ServiceRequestCompleted',
  'EngineConfigurationChanged',
  'StatesChanged', 'RelationshipStatesChanged', 'ActivityEvent',
] as const;

/** Raised by this client on itself, never by the server, and sent to the same handlers as the server's
 *  events, because the snapshot and the changes after it both say what the page holds. */
export const SUBSCRIPTION_OPENED = 'SubscriptionOpened';

/** Raised by this client with the new selector before the platform is asked, because what the page holds
 *  was loaded for the old coverage and would otherwise show as the new page's for a request or more. A
 *  reconnect covers the same and raises nothing. */
export const SUBSCRIPTION_CHANGING = 'SubscriptionChanging';

/** A browser delivers only the kinds a page listens for, so a handler for any other kind would never be
 *  called and nothing would say so. */
type StreamEventKind =
  | typeof KNOWN_EVENTS[number]
  | typeof SUBSCRIPTION_OPENED
  | typeof SUBSCRIPTION_CHANGING;

type Handler = (...eventArguments: unknown[]) => void;
type Entry = { event: StreamEventKind; handler: Handler };

// Kept apart from the connection, so the handlers are attached again after a reconnect.
const handlers = new Set<Entry>();
const listeners = new Set<() => void>();

let objectSource: EventSource | null = null;
let systemSource: EventSource | null = null;
let silenceWatches: SilenceWatch[] = [];
let openSubscriptionId: string | null = null;
let referenceCount = 0;
let connectionState: ConnectionState = 'connecting';
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let reconnectAttempt = 0;
let generation = 0; // raised on release and reconnect, so an older open still in progress stops

// The highest change sequence applied. Kept across reconnects, so the broker replays what came after it
// and drops repeats, rather than starting from now and missing everything while the stream was down.
// Cleared on release and when the coverage changes, because either makes the position meaningless;
// null starts from the snapshot's own position.
let consumedWatermark: number | null = null;

/** Innermost last. A page declares what it is about while on screen, so a dashboard is sent the Things it
 *  draws and the graph is sent everything; a leaving page restores what the page beneath it asked for, in
 *  whichever order the two change. */
const declared: { selector: SubscriptionSelector }[] = [];

/** The shell's declaration, held apart from the pages' stack, because a page mounts its declaration before
 *  the shell does, and the shell's must not win by arriving last. */
let defaultSelector: SubscriptionSelector = WHOLE_MODEL;

/** What the streams were opened with, so a declaration that changes nothing reopens nothing. */
let openedFor: string | null = null;

function effectiveSelector(): SubscriptionSelector {
  return declared.length ? declared[declared.length - 1].selector : defaultSelector;
}

function notify() { listeners.forEach((l) => l()); }
function setConnection(next: ConnectionState) { if (connectionState !== next) { connectionState = next; notify(); } }

/** The events whose payload is a property rather than an entity. Listed rather than matched on the
 *  name, so adding one is a decision about its shape instead of an accident of what it is called. */
const PROPERTY_EVENTS = new Set([
  'PropertyChanged', 'PropertyDeleted', 'RelationshipPropertyChanged', 'RelationshipPropertyDeleted',
  'PropertyObserved',
]);

// A property event's handler takes (identifier, name, value), with no value on a retraction; any other
// takes the data object.
function toArguments(kind: string, data: { EntityId?: string; PropertyName?: string; Value?: unknown } | unknown): unknown[] {
  if (PROPERTY_EVENTS.has(kind)) {
    const d = (data ?? {}) as { EntityId?: string; PropertyName?: string; Value?: unknown };
    return [d.EntityId, d.PropertyName, d.Value];
  }
  return [data];
}

function deliverToListeners(kind: string, data: unknown) {
  const eventArguments = toArguments(kind, data);
  handlers.forEach((h) => {
    if (h.event !== kind) return;
    try { h.handler(...eventArguments); } catch (err) { console.error(`SSE handler for ${kind} threw:`, err); }
  });
}

// Only the subscription stream's events carry the model's change sequence as their identifier.
function attachListeners(source: EventSource, trackWatermark = false) {
  const silence = watchForSilence(source, scheduleReconnect);
  silenceWatches.push(silence);
  for (const kind of KNOWN_EVENTS) {
    source.addEventListener(kind, (e: MessageEvent) => {
      // A replay sends changes and no heartbeat until it is done, so a change is a sign of life too.
      silence.heard();
      if (trackWatermark && e.lastEventId) {
        const sequence = Number(e.lastEventId);
        // A replay can send again what was applied, so the position never goes back.
        if (Number.isFinite(sequence) && (consumedWatermark === null || sequence > consumedWatermark)) {
          consumedWatermark = sequence;
        }
      }
      let data: unknown;
      try { data = e.data ? JSON.parse(e.data) : undefined; } catch { data = e.data; }
      deliverToListeners(kind, data);
    });
  }
}

/** Nothing expires a registry entry, and a page with its own subscription opens one per navigation, so an
 *  abandoned entry would be written to for the life of the process. Not awaited: a failed release costs one
 *  stale entry, and waiting would hold up the stream that replaces it. */
function releaseSubscription(id: string | null) {
  if (!id) return;
  void apiClient.ensureToken().then((token) =>
    fetch(`${BASE_URL}/api/subscriptions/${id}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${token}` },
    }).catch(() => {}),
  ).catch(() => {});
}

function closeStreams() {
  silenceWatches.forEach((watch) => watch.stop());
  silenceWatches = [];
  objectSource?.close();
  systemSource?.close();
  objectSource = null;
  systemSource = null;
  releaseSubscription(openSubscriptionId);
  openSubscriptionId = null;
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  setConnection('lost');
  const delays = [1000, 2000, 5000, 10000, 30000];
  const delay = delays[Math.min(reconnectAttempt, delays.length - 1)];
  reconnectAttempt++;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    if (referenceCount > 0) void openStreams();
  }, delay);
}

// EventSource cannot renew the token in its address on its own reconnect, so on an error this closes and
// reopens with a new token.
async function openStreams() {
  const myGeneration = ++generation;
  closeStreams();
  // Closing a stream that had failed is still a retry, so only a live one becomes connecting.
  if (connectionState === 'live') setConnection('connecting');
  const selector = effectiveSelector();
  openedFor = JSON.stringify(selector);
  const superseded = () => myGeneration !== generation || referenceCount === 0;
  // Every path but an open that attaches its streams hands this subscription back, because nothing would
  // read or expire it, and a declaration changing mid-open leaves one behind every time.
  let granted: string | null = null;
  try {
    const token = await apiClient.ensureToken();
    if (superseded()) return;

    // The model read fills the store for a whole-model page, so its snapshot is declined rather than the
    // whole model built, sent and parsed twice; the snapshot's position still starts the first resume.
    const response = await fetch(`${BASE_URL}/api/subscriptions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify(selector.all ? { ...selector, includeSnapshot: false } : selector),
    });
    if (!response.ok) {
      if (!superseded()) scheduleReconnect();
      return;
    }

    const { subscriptionId, watermark, snapshot } = await response.json();
    granted = subscriptionId;
    if (superseded()) return;

    // Minted after the snapshot call, never before: on a large model that call is the slow step, and
    // a credential that lives for minutes must not spend them waiting for it.
    const streamToken = await apiClient.mintStreamToken();
    if (superseded()) return;

    const tokenParameter = `access_token=${encodeURIComponent(streamToken)}&${HEARTBEAT_AS_EVENT}`;

    // On a reconnect the broker replays what was missed after this position and drops repeats; on a first
    // connect the position is the snapshot's. The stream prefers ?lastEventId to the subscription's own.
    const resumeFrom = consumedWatermark ?? watermark;
    consumedWatermark = resumeFrom;
    const object = new EventSource(
      `${BASE_URL}/api/subscriptions/${subscriptionId}/stream?${tokenParameter}&lastEventId=${resumeFrom}`,
    );
    object.onopen = () => { reconnectAttempt = 0; setConnection('live'); };
    object.onerror = () => scheduleReconnect();
    attachListeners(object, true);
    objectSource = object;

    const sys = new EventSource(`${BASE_URL}/api/events/stream?${tokenParameter}`);
    sys.onerror = () => scheduleReconnect();
    attachListeners(sys);
    systemSource = sys;

    openSubscriptionId = subscriptionId;
    granted = null; // released with its streams now, not here
    announceOpened(subscriptionId, watermark, snapshot, selector);
  } catch (err) {
    console.warn('SSE open failed; will retry:', err instanceof Error ? err.message : err);
    scheduleReconnect();
  } finally {
    releaseSubscription(granted);
  }
}

/** Raised after the streams are attached, never before: the snapshot names the moment the stream
 *  resumes from, and a reader given it while nothing was listening would apply it and then miss
 *  every change between the two. */
function announceOpened(
  subscriptionId: string,
  watermark: number,
  snapshot: Snapshot | undefined,
  selector: SubscriptionSelector,
) {
  const opened: SubscriptionOpened = {
    subscriptionId,
    watermark,
    covered: selector.all ? null : coveredBy(snapshot),
  };
  deliverToListeners(SUBSCRIPTION_OPENED, opened);
}

type Snapshot = { things?: (VosThing & { States: string[] })[]; relationships?: VosRelationship[] };

/** A snapshot in the shape the store holds: properties unwrapped as the model read gives them, and
 *  the states beside the Things rather than on them. */
function coveredBy(snapshot: Snapshot | undefined): CoveredObjects {
  const things = snapshot?.things ?? [];
  return {
    things: things.map(unwrapThing),
    relationships: (snapshot?.relationships ?? []).map(unwrapRelationship),
    thingStates: new Map(things.map((thing) => [thing.Id, thing.States])),
  };
}

/** A fixed set, because the platform follows later matches only for what a subscription opened with. Null
 *  while none is open; a caller that must add to whichever is open listens for {@link SUBSCRIPTION_OPENED}
 *  and asks again, because a reopened subscription starts from its own declaration alone. */
export async function addToSubscription(selector: SubscriptionSelector): Promise<CoveredObjects | null> {
  const subscriptionId = openSubscriptionId;
  if (!subscriptionId) return null;
  const token = await apiClient.ensureToken();
  const response = await fetch(`${BASE_URL}/api/subscriptions/${subscriptionId}/objects`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(selector),
  });
  if (!response.ok) throw new Error(`The subscription refused the addition: ${response.status}`);
  const { snapshot } = await response.json();
  return coveredBy(snapshot);
}

/** Take objects out of the open subscription that an addition put in. Nothing to take out of a
 *  subscription that is no longer open, since a reopened one never held them. */
export async function removeFromSubscription(ids: string[]): Promise<void> {
  const subscriptionId = openSubscriptionId;
  if (!subscriptionId || ids.length === 0) return;
  const token = await apiClient.ensureToken();
  const response = await fetch(`${BASE_URL}/api/subscriptions/${subscriptionId}/objects`, {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ ids }),
  });
  if (!response.ok) throw new Error(`The subscription refused the removal: ${response.status}`);
}

/** How long an arriving page is given to make its declaration. Long enough for its code to be
 *  fetched and mounted; a page slower than this costs one snapshot built for no reader, and
 *  nothing else — the subscription still settles on what that page asked for. */
const DECLARATIONS_SETTLE_MILLISECONDS = 300;

/** The applied position goes with the old coverage, because replaying from it would apply changes the new
 *  snapshot already holds, and could ask for a range the broker no longer keeps. */
function follow() {
  settling = null;
  if (referenceCount === 0) return;
  const selector = effectiveSelector();
  if (JSON.stringify(selector) === openedFor) return;
  consumedWatermark = null;
  deliverToListeners(SUBSCRIPTION_CHANGING, selector);
  void openStreams();
}

/**
 * A page replacing its own declaration does so in one commit, so there is nothing to wait for. What is
 * waited out is no page declaring at all, a page gone while the next one's code loads, because following
 * that opens a subscription nobody reads. There is no wait before the first open, when no page hands over.
 */
let settling: ReturnType<typeof setTimeout> | null = null;
let following = false;
function followDeclarations() {
  if (following || settling) return;
  following = true;
  queueMicrotask(() => {
    following = false;
    if (openedFor !== null && declared.length === 0) settling = setTimeout(follow, DECLARATIONS_SETTLE_MILLISECONDS);
    else follow();
  });
}

/** Open the declared subscription again, from a fresh snapshot. What a subscription covers is
 *  resolved when it opens, so a model replaced under it has to be asked for again. */
export function resubscribe(): void {
  if (referenceCount === 0) return;
  // A settle still pending would open a second time for the same declaration.
  if (settling) { clearTimeout(settling); settling = null; }
  consumedWatermark = null;
  void openStreams();
}

/** Compared by what it says rather than by identity, so a page may rebuild the selector on every render. */
export function useSubscription(selector: SubscriptionSelector): void {
  const declaration = JSON.stringify(selector);
  useEffect(() => {
    const entry = { selector: JSON.parse(declaration) as SubscriptionSelector };
    declared.push(entry);
    followDeclarations();
    return () => {
      const at = declared.indexOf(entry);
      if (at >= 0) declared.splice(at, 1);
      followDeclarations();
    };
  }, [declaration]);
}

/** The shell's declaration: what the subscription covers on a page that asks for nothing of its
 *  own. A page's declaration always wins over it, however the two mount. */
export function useDefaultSubscription(selector: SubscriptionSelector): void {
  const declaration = JSON.stringify(selector);
  useEffect(() => {
    defaultSelector = JSON.parse(declaration) as SubscriptionSelector;
    followDeclarations();
    return () => {
      defaultSelector = WHOLE_MODEL;
      followDeclarations();
    };
  }, [declaration]);
}

/** The first open goes through the same settling as any later one, so it is made with what the
 *  mounted pages declared rather than with the whole model they were about to narrow. */
function acquire() {
  referenceCount++;
  if (referenceCount === 1) followDeclarations();
}

function release() {
  referenceCount--;
  if (referenceCount <= 0) {
    referenceCount = 0;
    generation++;
    if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
    if (settling) { clearTimeout(settling); settling = null; }
    reconnectAttempt = 0;
    consumedWatermark = null; // the position belongs to one model, so a fresh start begins from the newest
    openedFor = null;
    closeStreams();
    setConnection('connecting');
  }
}

export function useSse() {
  useEffect(() => {
    acquire();
    return () => release();
  }, []);

  const connection = useSyncExternalStore(
    (callback) => { listeners.add(callback); return () => listeners.delete(callback); },
    () => connectionState,
  );

  const on = useCallback((event: StreamEventKind, handler: Handler) => {
    const entry: Entry = { event, handler };
    handlers.add(entry);
    return () => { handlers.delete(entry); };
  }, []);

  return { connection, on };
}
