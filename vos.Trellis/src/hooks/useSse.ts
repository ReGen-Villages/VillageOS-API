import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';

// Replaces useSignalR (#5588): live model + operational updates over Server-Sent Events.
// Two streams — the whole-model object subscription and the system/operational events —
// feed one dispatch surface. Same { connected, on } API the consumers used with SignalR.

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

// Event kinds the server emits on either stream (object subscription + system events).
const KNOWN_EVENTS = [
  'ModelChanged', 'ModelCleared',
  'ThingCreated', 'ThingDeleted', 'RelationshipCreated', 'RelationshipDeleted',
  'PropertyChanged', 'PropertyDeleted', 'RelationshipPropertyChanged',
  'ServiceHealthChanged', 'DaemonStatusChanged', 'EndpointServiceRequestCompleted',
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

function notify() { listeners.forEach((l) => l()); }
function setConnected(v: boolean) { if (connectedState !== v) { connectedState = v; notify(); } }

// Map an SSE event's data object to the positional args the legacy SignalR handlers expect.
// Property changes are (id, name, value); everything else passes the data object through.
function toArgs(kind: string, data: { EntityId?: string; PropertyName?: string; Value?: unknown } | unknown): unknown[] {
  if (kind === 'PropertyChanged' || kind === 'RelationshipPropertyChanged') {
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

function attachListeners(source: EventSource) {
  for (const kind of KNOWN_EVENTS) {
    source.addEventListener(kind, (e: MessageEvent) => {
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

    const tokenParam = `access_token=${encodeURIComponent(token)}`;

    // Whole-model object subscription: snapshot watermark anchors the first resume.
    const resp = await fetch(`${BASE_URL}/api/subscriptions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ all: true }),
    });
    if (myGeneration !== generation || refCount === 0) return;
    if (resp.ok) {
      const { subscriptionId, watermark } = await resp.json();
      if (myGeneration !== generation || refCount === 0) return;
      const obj = new EventSource(
        `${BASE_URL}/api/subscriptions/${subscriptionId}/stream?${tokenParam}&lastEventId=${watermark}`,
      );
      obj.onopen = () => { reconnectAttempt = 0; setConnected(true); };
      obj.onerror = () => scheduleReconnect();
      attachListeners(obj);
      objectSource = obj;
    } else {
      scheduleReconnect();
      return;
    }

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
