import { HubConnectionBuilder, HubConnection, LogLevel, HubConnectionState } from '@microsoft/signalr';
import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';

// Module-level singleton — shared across all hook consumers
let sharedConnection: HubConnection | null = null;
let refCount = 0;
let connectedState = false;
const listeners = new Set<() => void>();

// Hub handlers, registered independently of any single HubConnection instance.
// The connection can be torn down and rebuilt (StrictMode remount, refCount
// cycling, reconnect) — when that happens we must re-attach every handler to
// the new connection, otherwise the server pushes to a live socket that has no
// client methods and the GUI silently drops PropertyChanged/ActivityEvent
// (Bug #5527: no node flash, no live panel update, no activity feed).
type HubHandler = { event: string; handler: (...args: unknown[]) => void };
const hubHandlers = new Set<HubHandler>();

function attachAllHandlers(connection: HubConnection) {
  hubHandlers.forEach(({ event, handler }) => connection.on(event, handler));
}

function notifyListeners() {
  listeners.forEach((l) => l());
}

/**
 * Retry the initial SignalR `.start()` with exponential backoff.
 * `withAutomaticReconnect` only handles drops of an *established* connection —
 * it does NOT retry the initial handshake, so we handle that here.
 */
async function startWithRetry(connection: HubConnection) {
  const delays = [0, 2000, 5000, 10000, 30000];
  for (let i = 0; i < delays.length; i++) {
    try {
      if (delays[i] > 0) await new Promise((r) => setTimeout(r, delays[i]));
      // Bug #5527: bail if this connection was released/replaced while we were
      // waiting. Otherwise a lingering retry loop resurrects an orphaned socket
      // that the server then pushes to — but its handlers live on the current
      // sharedConnection, so those messages are dropped.
      if (connection !== sharedConnection) return;
      if (connection.state !== HubConnectionState.Disconnected) return; // already started
      await connection.start();
      connectedState = true;
      notifyListeners();
      return;
    } catch (err) {
      console.warn(
        `SignalR connection attempt ${i + 1}/${delays.length} failed:`,
        err instanceof Error ? err.message : err,
      );
    }
  }
  console.error('SignalR: all connection attempts failed');
}

function acquireConnection(): HubConnection {
  if (!sharedConnection) {
    const connection = new HubConnectionBuilder()
      .withUrl('/vosHub', {
        // Return a Promise<string> — fetches a fresh token if the cache is empty
        // or expired. Previously this called getToken() which returned null on
        // first load, causing Mycelium to reject the WebSocket upgrade.
        accessTokenFactory: () => apiClient.ensureToken(),
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    connection.onreconnected(() => {
      connectedState = true;
      notifyListeners();
    });
    connection.onreconnecting(() => {
      connectedState = false;
      notifyListeners();
    });
    connection.onclose(() => {
      connectedState = false;
      notifyListeners();
    });

    // Re-attach any handlers registered before this (re)build so a rebuilt
    // connection is never left without client methods (Bug #5527).
    attachAllHandlers(connection);

    // Publish as the shared connection *before* starting so the startWithRetry
    // orphan-abort guard (connection === sharedConnection) holds.
    sharedConnection = connection;

    // Start with retry — handles Mycelium not ready, token fetch failures, etc.
    startWithRetry(connection);
  }
  refCount++;
  return sharedConnection;
}

function releaseConnection() {
  refCount--;
  if (refCount <= 0 && sharedConnection) {
    sharedConnection.stop();
    sharedConnection = null;
    refCount = 0;
    connectedState = false;
    notifyListeners();
  }
}

function subscribe(onStoreChange: () => void) {
  listeners.add(onStoreChange);
  return () => listeners.delete(onStoreChange);
}

function getSnapshot() {
  return connectedState;
}

export function useSignalR() {
  useEffect(() => {
    acquireConnection();
    return () => releaseConnection();
  }, []);

  const connected = useSyncExternalStore(subscribe, getSnapshot);

  const on = useCallback((event: string, handler: (...args: unknown[]) => void) => {
    // Register in the connection-independent registry so the handler is
    // re-attached across connection rebuilds (Bug #5527), and attach it to the
    // live connection now if one exists.
    const entry: HubHandler = { event, handler };
    hubHandlers.add(entry);
    sharedConnection?.on(event, handler);
    return () => {
      hubHandlers.delete(entry);
      sharedConnection?.off(event, handler);
    };
  }, []);

  const isConnected = useCallback(() => {
    return sharedConnection?.state === HubConnectionState.Connected;
  }, []);

  return { connected, on, isConnected };
}
