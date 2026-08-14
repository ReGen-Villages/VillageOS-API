import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useSse } from './useSse';
import { apiClient } from '../api/client';

// Captures EventSource instances + their per-event listeners so a test can fire server pushes.
class FakeEventSource {
  static instances: FakeEventSource[] = [];
  url: string;
  listeners = new Map<string, (e: MessageEvent) => void>();
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  constructor(url: string) { this.url = url; FakeEventSource.instances.push(this); }
  addEventListener(type: string, cb: (e: MessageEvent) => void) { this.listeners.set(type, cb); }
  close() { this.closed = true; }
  emit(type: string, data: unknown, id?: number | string) {
    this.listeners.get(type)?.({
      data: JSON.stringify(data),
      lastEventId: id != null ? String(id) : '',
    } as MessageEvent);
  }
}

vi.mock('../api/client', () => ({
  apiClient: {
    ensureToken: vi.fn().mockResolvedValue('sign-in-token'),
    mintStreamToken: vi.fn().mockResolvedValue('stream-token'),
  },
}));

describe('useSse', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    (globalThis as unknown as { EventSource: unknown }).EventSource = FakeEventSource;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ subscriptionId: 's1', watermark: 0 }),
    }) as unknown as typeof fetch;
  });

  afterEach(() => vi.restoreAllMocks());

  it('maps a PropertyChanged event to legacy positional args', async () => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on('PropertyChanged', handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
    act(() => FakeEventSource.instances[0].emit('PropertyChanged',
      { EntityId: 't1', PropertyName: 'temp', Value: 5 }));

    expect(handler).toHaveBeenCalledWith('t1', 'temp', 5);
    act(() => off());
    unmount();
  });

  // Every property event has to reach a handler as (id, name, value), retractions included. An
  // event missing from that set arrives as a raw object instead, and the handler quietly does
  // nothing — no error, no update, the change simply never lands.
  it.each([
    ['PropertyChanged', { EntityId: 't1', PropertyName: 'temp', Value: 5 }, ['t1', 'temp', 5]],
    ['PropertyDeleted', { EntityId: 't1', PropertyName: 'temp' }, ['t1', 'temp', undefined]],
    ['RelationshipPropertyChanged', { EntityId: 'r1', PropertyName: 'total', Value: 9 }, ['r1', 'total', 9]],
    ['RelationshipPropertyDeleted', { EntityId: 'r1', PropertyName: 'total' }, ['r1', 'total', undefined]],
  ])('maps %s to positional args', async (event, payload, expected) => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(event as string, handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
    act(() => FakeEventSource.instances[0].emit(event as string, payload));

    expect(handler).toHaveBeenCalledWith(...(expected as unknown[]));
    act(() => off());
    unmount();
  });

  it('passes operational event data through as a single object', async () => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on('ActivityEvent', handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
    act(() => FakeEventSource.instances[0].emit('ActivityEvent', { Type: 'X', Description: 'hi' }));

    expect(handler).toHaveBeenCalledWith({ Type: 'X', Description: 'hi' });
    act(() => off());
    unmount();
  });

  it('opens both the object subscription and the system-events stream', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(FakeEventSource.instances.length).toBe(2));

    const urls = FakeEventSource.instances.map((e) => e.url);
    expect(urls.some((u) => u.includes('/api/subscriptions/s1/stream'))).toBe(true);
    expect(urls.some((u) => u.includes('/api/events/stream'))).toBe(true);
    expect(urls.every((u) => u.includes('access_token=stream-token'))).toBe(true);
    unmount();
  });

  // The address is recorded — access logs, proxies, browser history — so of the two credentials the
  // hook now holds, only the short-lived one may be written into it.
  it('carries a stream token in the address and never the sign-in token', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(FakeEventSource.instances.length).toBe(2));

    expect(FakeEventSource.instances.map((e) => e.url).some((u) => u.includes('sign-in-token')))
      .toBe(false);
    unmount();
  });

  it('mints a fresh stream token for each reconnect', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const mintsForTheFirstOpen = vi.mocked(apiClient.mintStreamToken).mock.calls.length;

    vi.useFakeTimers();
    act(() => objectStreams()[0].onerror?.());
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(vi.mocked(apiClient.mintStreamToken).mock.calls.length)
      .toBeGreaterThan(mintsForTheFirstOpen);
    unmount();
  });

  const objectStreams = () => FakeEventSource.instances.filter((e) => e.url.includes('/subscriptions/'));

  // Bug #5943: the first connect has no consumed position, so it seeds from the snapshot
  // watermark; after consuming events, a reconnect must resume from the consumed sequence so
  // the broker replays only the gap — not re-subscribe at the current head (skipping the gap).
  it('resumes the object stream from the consumed sequence on reconnect', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    expect(objectStreams()[0].url).toContain('lastEventId=0'); // first connect: snapshot watermark

    const obj0 = objectStreams()[0];
    act(() => obj0.emit('ThingCreated', { EntityId: 't7' }, 7)); // consume up to sequence 7

    vi.useFakeTimers();
    act(() => obj0.onerror?.());               // stream drops → schedules reconnect
    await vi.advanceTimersByTimeAsync(1000);   // first backoff delay
    vi.useRealTimers();

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(objectStreams()[1].url).toContain('lastEventId=7'); // resumed from consumed sequence
    unmount();
  });

  it('resets the watermark on full teardown so a model switch restarts from the fresh snapshot', async () => {
    const first = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].emit('ThingCreated', { EntityId: 't7' }, 7));
    first.unmount(); // release() → consumedWatermark reset

    // A different model's snapshot head is 99; the remount must resume from 99, not the stale 7.
    FakeEventSource.instances = [];
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ subscriptionId: 's2', watermark: 99 }),
    }) as unknown as typeof fetch;

    const second = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    expect(objectStreams()[0].url).toContain('lastEventId=99');
    expect(objectStreams()[0].url).not.toContain('lastEventId=7');
    second.unmount();
  });
});
