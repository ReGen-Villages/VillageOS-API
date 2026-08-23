import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';
import { unwrapThing, unwrapRelationship } from '../utils/propertyMapper';
import { WHOLE_MODEL, type SubscriptionOpened, type SubscriptionSelector } from '../types/subscription';
import type { VosThing, VosRelationship } from '../types/vos';

// Live model + operational updates over Server-Sent Events.
// Two streams — the object subscription the page declared and the system/operational events —
// feed one dispatch surface. Same { connected, on } API the consumers use.

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

// Event kinds the server emits on either stream (object subscription + system events).
const KNOWN_EVENTS = [
  'ModelChanged', 'ModelCleared',
  'ThingCreated', 'ThingDeleted', 'RelationshipCreated', 'RelationshipDeleted',
  'PropertyChanged', 'PropertyDeleted', 'RelationshipPropertyChanged', 'RelationshipPropertyDeleted',
  'ServiceHealthChanged', 'DaemonStatusChanged', 'EndpointServiceRequestCompleted', 'ServiceRequestCompleted',
  'StatesChanged', 'RelationshipStatesChanged', 'ActivityEvent',
];

/** A subscription has opened, with the objects it covers as they stood at that moment. Dispatched
 *  to the same handlers the server's own events reach, rather than through a channel of its own: a
 *  subscription answers with a snapshot and then streams the changes to it, and both halves say
 *  what the page holds. Never sent by the server — this client raises it on itself. */
export const SUBSCRIPTION_OPENED = 'SubscriptionOpened';

type Handler = (...args: unknown[]) => void;
type Entry = { event: string; handler: Handler };

// Connection-independent handler registry (re-attached across reconnects, like the old hub).
const handlers = new Set<Entry>();
const listeners = new Set<() => void>();

let objectSource: EventSource | null = null;
let systemSource: EventSource | null = null;
let openSubscriptionId: string | null = null;
let refCount = 0;
let connectedState = false;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let reconnectAttempt = 0;
let generation = 0; // bumped on release/reconnect to abort stale async opens

// Highest object-stream sequence (SSE event id) this client has applied (Bug #5943).
// Persists across reconnects so we resume from here — the broker replays committed Facts
// after it and de-dupes by sequence, closing the disconnect gap precisely instead of
// re-subscribing at the current head (which skipped everything during the drop). Reset on
// full teardown / model switch (release) and on a change of what the subscription covers,
// because either makes the position meaningless. null = no position yet → start from the
// fresh snapshot watermark.
let consumedWatermark: number | null = null;

/**
 * What the mounted pages have asked the subscription to cover, innermost last.
 *
 * A page declares what it is about while it is on screen and takes the declaration back when it
 * leaves, so the subscription follows the page rather than the app: a dashboard is sent the handful
 * of Things it draws, and the graph — which genuinely reads across the model — is sent everything.
 * The stack is what makes the declaration a page's own: a page leaving restores whatever the page
 * beneath it asked for, whichever order the two changed in.
 */
const declared: { selector: SubscriptionSelector }[] = [];

/** What the subscription covers when no page has declared anything — the shell's own declaration.
 *  Held apart from the pages' rather than at the bottom of their stack, because a page mounts its
 *  declaration before the shell containing it mounts one, and the shell's must not win by arriving
 *  last. */
let defaultSelector: SubscriptionSelector = WHOLE_MODEL;

/** What the streams were opened with, so a declaration that changes nothing reopens nothing. */
let openedFor: string | null = null;

function effectiveSelector(): SubscriptionSelector {
  return declared.length ? declared[declared.length - 1].selector : defaultSelector;
}

function notify() { listeners.forEach((l) => l()); }
function setConnected(v: boolean) { if (connectedState !== v) { connectedState = v; notify(); } }

/** The events whose payload is a property rather than an entity. Listed rather than matched on the
 *  name, so adding one is a decision about its shape instead of an accident of what it is called. */
const PROPERTY_EVENTS = new Set([
  'PropertyChanged', 'PropertyDeleted', 'RelationshipPropertyChanged', 'RelationshipPropertyDeleted',
]);

// Map an SSE event's data object to the positional args the handlers expect.
// A property event is (id, name, value), with no value on a retraction; everything else passes the
// data object through.
function toArgs(kind: string, data: { EntityId?: string; PropertyName?: string; Value?: unknown } | unknown): unknown[] {
  if (PROPERTY_EVENTS.has(kind)) {
    const d = (data ?? {}) as { EntityId?: string; PropertyName?: string; Value?: unknown };
    return [d.EntityId, d.PropertyName, d.Value];
  }
  return [data];
}

function dispatch(kind: string, data: unknown) {
  const args = toArgs(kind, data);
  handlers.forEach((h) => {
    if (h.event !== kind) return;
    try { h.handler(...args); } catch (err) { console.error(`SSE handler for ${kind} threw:`, err); }
  });
}

// `trackWatermark` is true only for the object subscription — its events carry the
// per-model Fact sequence as their SSE id; the system/operational stream does not.
function attachListeners(source: EventSource, trackWatermark = false) {
  for (const kind of KNOWN_EVENTS) {
    source.addEventListener(kind, (e: MessageEvent) => {
      if (trackWatermark && e.lastEventId) {
        const seq = Number(e.lastEventId);
        // Monotonic guard: replay/de-dup can re-deliver ≤ our position; never rewind.
        if (Number.isFinite(seq) && (consumedWatermark === null || seq > consumedWatermark)) {
          consumedWatermark = seq;
        }
      }
      let data: unknown;
      try { data = e.data ? JSON.parse(e.data) : undefined; } catch { data = e.data; }
      dispatch(kind, data);
    });
  }
}

/** Hand the subscription back. Nothing expires a registry entry (Bug #6562), and a page that
 *  declares its own subscription opens one per navigation, so an abandoned entry would go on being
 *  written to for the life of the process. Best effort: a failed release costs one stale entry, and
 *  waiting on it would hold up the stream that replaces it. */
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
  objectSource?.close();
  systemSource?.close();
  objectSource = null;
  systemSource = null;
  releaseSubscription(openSubscriptionId);
  openSubscriptionId = null;
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  setConnected(false);
  const delays = [1000, 2000, 5000, 10000, 30000];
  const delay = delays[Math.min(reconnectAttempt, delays.length - 1)];
  reconnectAttempt++;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    if (refCount > 0) void openStreams();
  }, delay);
}

// Opens both streams with a fresh token. EventSource can't refresh the URL token on its own
// reconnect, so we drive reconnect ourselves (close + reopen with a new token) on error.
async function openStreams() {
  const myGeneration = ++generation;
  closeStreams();
  const selector = effectiveSelector();
  openedFor = JSON.stringify(selector);
  try {
    const token = await apiClient.ensureToken();
    if (myGeneration !== generation || refCount === 0) return; // released/superseded while awaiting

    // The subscription the mounted page declared. Its snapshot watermark anchors the first resume.
    const resp = await fetch(`${BASE_URL}/api/subscriptions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify(selector),
    });
    if (myGeneration !== generation || refCount === 0) return;
    if (!resp.ok) {
      scheduleReconnect();
      return;
    }

    const { subscriptionId, watermark, snapshot } = await resp.json();
    if (myGeneration !== generation || refCount === 0) return;
    openSubscriptionId = subscriptionId;

    // Minted after the snapshot call, never before: on a large model that call is the slow step, and
    // a credential that lives for minutes must not spend them waiting for it.
    const streamToken = await apiClient.mintStreamToken();
    if (myGeneration !== generation || refCount === 0) return;

    const tokenParam = `access_token=${encodeURIComponent(streamToken)}`;

    // Resume from where we left off (Bug #5943): on a reconnect the broker replays the
    // Facts we missed and de-dupes by sequence; on a first connect we have no position, so
    // seed from the fresh snapshot watermark. The stream honours ?lastEventId over the
    // subscription's own watermark, so a fresh subscription still resumes precisely.
    const resumeFrom = consumedWatermark ?? watermark;
    consumedWatermark = resumeFrom;
    const obj = new EventSource(
      `${BASE_URL}/api/subscriptions/${subscriptionId}/stream?${tokenParam}&lastEventId=${resumeFrom}`,
    );
    obj.onopen = () => { reconnectAttempt = 0; setConnected(true); };
    obj.onerror = () => scheduleReconnect();
    attachListeners(obj, true);
    objectSource = obj;

    // System / operational events.
    const sys = new EventSource(`${BASE_URL}/api/events/stream?${tokenParam}`);
    sys.onerror = () => scheduleReconnect();
    attachListeners(sys);
    systemSource = sys;

    announceOpened(subscriptionId, watermark, snapshot, selector);
  } catch (err) {
    console.warn('SSE open failed; will retry:', err instanceof Error ? err.message : err);
    scheduleReconnect();
  }
}

/** Raised after the streams are attached, never before: the snapshot names the moment the stream
 *  resumes from, and a reader given it while nothing was listening would apply it and then miss
 *  every change between the two. */
function announceOpened(
  subscriptionId: string,
  watermark: number,
  snapshot: { things?: VosThing[]; relationships?: VosRelationship[] } | undefined,
  selector: SubscriptionSelector,
) {
  const opened: SubscriptionOpened = {
    subscriptionId,
    watermark,
    covered: selector.all ? null : {
      things: (snapshot?.things ?? []).map(unwrapThing),
      relationships: (snapshot?.relationships ?? []).map(unwrapRelationship),
    },
  };
  dispatch(SUBSCRIPTION_OPENED, opened);
}

/**
 * How long the declarations are given to settle before the subscription follows them.
 *
 * A navigation takes the leaving page's declaration back before the arriving page makes its own,
 * and the arriving page's code is fetched on demand, so the gap between the two is a load rather
 * than a tick. Acting inside it opens a subscription nobody asked for and throws it away — and
 * between two pages that both read the whole model, that is a whole-model snapshot built and a
 * whole model re-read for no reader. A page slower to arrive than this costs that, and nothing
 * else: the subscription still settles on what it asked for.
 */
const DECLARATIONS_SETTLE_MS = 300;

/**
 * Reopen when the mounted pages have changed what the subscription should cover.
 *
 * The first open waits only for the declarations already being made in this commit — there is no
 * page to hand over from, and every moment before it is a page waiting on its data.
 *
 * The consumed position goes with the old coverage: the new subscription answers with its own
 * snapshot, and replaying from a position taken under different coverage would re-apply changes
 * that snapshot already holds and could ask for a range the broker no longer retains.
 */
let settling: ReturnType<typeof setTimeout> | null = null;
function followDeclarations() {
  if (settling) return;
  const follow = () => {
    settling = null;
    if (refCount === 0) return;
    if (JSON.stringify(effectiveSelector()) === openedFor) return;
    consumedWatermark = null;
    void openStreams();
  };
  if (openedFor === null) queueMicrotask(follow);
  else settling = setTimeout(follow, DECLARATIONS_SETTLE_MS);
}

/** Open the declared subscription again, from a fresh snapshot. What a subscription covers is
 *  resolved when it opens, so a model replaced under it has to be asked for again. */
export function resubscribe(): void {
  if (refCount === 0) return;
  consumedWatermark = null;
  void openStreams();
}

/**
 * Declare what the subscription must cover for as long as the caller is on screen. Leaving restores
 * whatever the rest of the mounted pages asked for.
 *
 * The selector is compared by what it says rather than by identity, so a page may rebuild it on
 * every render.
 */
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
  refCount++;
  if (refCount === 1) followDeclarations();
}

function release() {
  refCount--;
  if (refCount <= 0) {
    refCount = 0;
    generation++; // abort any in-flight open
    if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
    if (settling) { clearTimeout(settling); settling = null; }
    reconnectAttempt = 0;
    consumedWatermark = null; // per-model sequence — a fresh acquire (e.g. model switch) restarts from head
    openedFor = null;
    closeStreams();
    setConnected(false);
  }
}

export function useSse() {
  useEffect(() => {
    acquire();
    return () => release();
  }, []);

  const connected = useSyncExternalStore(
    (cb) => { listeners.add(cb); return () => listeners.delete(cb); },
    () => connectedState,
  );

  const on = useCallback((event: string, handler: Handler) => {
    const entry: Entry = { event, handler };
    handlers.add(entry);
    return () => { handlers.delete(entry); };
  }, []);

  return { connected, on };
}
