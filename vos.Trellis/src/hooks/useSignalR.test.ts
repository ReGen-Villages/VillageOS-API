import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

// Fake HubConnection that records .on/.off and lets tests fire server pushes.
class FakeConnection {
  state = 'Disconnected';
  handlers = new Map<string, (...args: unknown[]) => void>();
  on = vi.fn((event: string, cb: (...args: unknown[]) => void) => this.handlers.set(event, cb));
  off = vi.fn((event: string, cb: (...args: unknown[]) => void) => {
    if (this.handlers.get(event) === cb) this.handlers.delete(event);
  });
  start = vi.fn(async () => {
    this.state = 'Connected';
  });
  stop = vi.fn(async () => {
    this.state = 'Disconnected';
  });
  onreconnected = vi.fn();
  onreconnecting = vi.fn();
  onclose = vi.fn();
  // simulate a server-to-client hub invocation
  push(event: string, ...args: unknown[]) {
    this.handlers.get(event)?.(...args);
  }
}

const builtConnections: FakeConnection[] = [];

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
  LogLevel: { Information: 1 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const c = new FakeConnection();
      builtConnections.push(c);
      return c;
    }
  },
}));

vi.mock('../api/client', () => ({
  apiClient: { ensureToken: () => Promise.resolve('test-token') },
}));

import { useSignalR } from './useSignalR';

describe('useSignalR', () => {
  beforeEach(() => {
    builtConnections.length = 0;
  });

  it('delivers server pushes to handlers registered via on()', () => {
    const { result, unmount } = renderHook(() => useSignalR());
    const handler = vi.fn();
    act(() => {
      result.current.on('PropertyChanged', handler);
    });

    const conn = builtConnections.at(-1)!;
    act(() => conn.push('PropertyChanged', 't1', 'Dimensions.Length', 42));

    expect(handler).toHaveBeenCalledWith('t1', 'Dimensions.Length', 42);
    unmount();
  });

  // Bug #5527: when the shared connection is torn down and rebuilt, previously
  // registered handlers must be re-attached to the new connection — otherwise
  // the server pushes to a live socket with no client methods and the GUI
  // silently drops the update (no node flash / panel / activity-feed update).
  it('re-attaches handlers to a rebuilt connection so pushes are not dropped', () => {
    const first = renderHook(() => useSignalR());
    const handler = vi.fn();
    // Register without unsubscribing — the registry, not the component, owns
    // survival across rebuilds.
    act(() => {
      first.result.current.on('PropertyChanged', handler);
    });
    const conn1 = builtConnections.at(-1)!;
    expect(conn1.handlers.has('PropertyChanged')).toBe(true);

    // Tear the shared connection down (refCount -> 0 stops + nulls it), then
    // build a fresh one by acquiring again.
    first.unmount();
    expect(conn1.stop).toHaveBeenCalled();

    const second = renderHook(() => useSignalR());
    const conn2 = builtConnections.at(-1)!;
    expect(conn2).not.toBe(conn1);

    // The surviving handler must be attached to the new connection and a server
    // push on it must reach the handler.
    expect(conn2.handlers.has('PropertyChanged')).toBe(true);
    act(() => conn2.push('PropertyChanged', 't2', 'p', 7));
    expect(handler).toHaveBeenCalledWith('t2', 'p', 7);
    second.unmount();
  });

  it('does not resurrect a connection that was released before start (orphan-abort)', async () => {
    const first = renderHook(() => useSignalR());
    const conn1 = builtConnections.at(-1)!;
    first.unmount(); // releaseConnection -> conn1.stop(), sharedConnection = null

    const second = renderHook(() => useSignalR());
    const conn2 = builtConnections.at(-1)!;

    expect(conn2).not.toBe(conn1);
    expect(conn1.stop).toHaveBeenCalled();
    second.unmount();
  });
});
