import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';

// Live model + operational updates over Server-Sent Events.
// Two streams — the whole-model object subscription and the system/operational events —
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

type Handler = (...args: unknown[]) => void;
type Entry = { event: string; handler: Handler };

// Connection-independent handler registry (re-attached across reconnects, like the old hub).
const handlers = new Set<Entry>();
const listeners = new Set<() => void>();

let objectSource: EventSource | null = null;
let systemSource: EventSource | null = null;
let refCount = 0;
let connectedState = false;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let reconnectAttempt = 0;
let generation = 0; // bumped on release/reconnect to abort stale async opens

// Highest object-stream sequence (SSE event id) this client has applied (Bug #5943).
// Persists across reconnects so we resume from here — the broker replays committed Facts
// after it and de-dupes by sequence, closing the disconnect gap precisely instead of
// re-subscribing at the current head (which skipped everything during the drop). Reset on
// full teardown / model switch (release) because the sequence is per-model. null = no
// position yet → start from the fresh snapshot watermark.
let consumedWatermark: number | null = null;

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

function closeStreams() {
  objectSource?.close();
  systemSource?.close();
  objectSource = null;
  systemSource = null;
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
  try {
    const token = await apiClient.ensureToken();
    if (myGeneration !== generation || refCount === 0) return; // released/superseded while awaiting

    // Whole-model object subscription: snapshot watermark anchors the first resume.
    const resp = await fetch(`${BASE_URL}/api/subscriptions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ all: true }),
    });
    if (myGeneration !== generation || refCount === 0) return;
    if (!resp.ok) {
      scheduleReconnect();
      return;
    }

    const { subscriptionId, watermark } = await resp.json();
    if (myGeneration !== generation || refCount === 0) return;

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
  } catch (err) {
    console.warn('SSE open failed; will retry:', err instanceof Error ? err.message : err);
    scheduleReconnect();
  }
}

function acquire() {
  refCount++;
  if (refCount === 1) void openStreams();
}

function release() {
  refCount--;
  if (refCount <= 0) {
    refCount = 0;
    generation++; // abort any in-flight open
    if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
    reconnectAttempt = 0;
    consumedWatermark = null; // per-model sequence — a fresh acquire (e.g. model switch) restarts from head
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
