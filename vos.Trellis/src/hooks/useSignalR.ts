import { HubConnectionBuilder, HubConnection, LogLevel, HubConnectionState } from '@microsoft/signalr';
import { useEffect, useCallback, useSyncExternalStore } from 'react';
import { apiClient } from '../api/client';

// Module-level singleton — shared across all hook consumers
let sharedConnection: HubConnection | null = null;
let refCount = 0;
let connectedState = false;
const listeners = new Set<() => void>();

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

    // Start with retry — handles Mycelium not ready, token fetch failures, etc.
    startWithRetry(connection);

    sharedConnection = connection;
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
    const conn = sharedConnection;
    if (!conn) return () => {};
    conn.on(event, handler);
    return () => conn.off(event, handler);
  }, []);

  const isConnected = useCallback(() => {
    return sharedConnection?.state === HubConnectionState.Connected;
  }, []);

  return { connected, on, isConnected };
}
