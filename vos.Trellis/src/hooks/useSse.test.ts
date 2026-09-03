import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { SUBSCRIPTION_OPENED, resubscribe, useSse, useSubscription, useDefaultSubscription } from './useSse';
import { apiClient } from '../api/client';
import type { SubscriptionSelector, SubscriptionOpened } from '../types/subscription';

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

  /** The bodies of every subscription this test opened, oldest first. */
  const subscriptionsOpened = () =>
    vi.mocked(globalThis.fetch).mock.calls
      .filter(([, init]) => (init as RequestInit | undefined)?.method === 'POST')
      .map(([, init]) => JSON.parse((init as RequestInit).body as string));

  /** A page arriving with a declaration of its own. Unmounting it is that page leaving. */
  const mountPage = (selector: SubscriptionSelector) => renderHook(() => useSubscription(selector));

  it('opens the subscription the mounted page declared, and restores the last one when it leaves', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ types: ['Site'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(subscriptionsOpened()[1]).toEqual({ types: ['Site'] });

    page.unmount();
    await waitFor(() => expect(objectStreams().length).toBe(3));
    expect(subscriptionsOpened()[2]).toEqual({ all: true });
    unmount();
  });

  // Reopening costs a snapshot the platform has to build, so a page that re-renders — or one that
  // declares the same thing the page before it did — must not pay for one.
  it('reopens nothing when a declaration says what is already open', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ all: true });
    await act(async () => {});

    expect(objectStreams().length).toBe(1);
    page.unmount();
    unmount();
  });

  // A page mounts its declaration before the shell that contains it mounts one. Were the shell's
  // just another entry on the stack, arriving last would make it win and every page would be sent
  // the shell's handful of Things instead of what it asked for.
  it('keeps the shell\'s declaration under the page\'s, whichever mounts first', async () => {
    const { unmount } = renderHook(() => {
      useSse();
      useSubscription({ types: ['Site'] });   // the page, mounted first
      useDefaultSubscription({ types: ['Dashboard'] });
    });

    await waitFor(() => expect(objectStreams().length).toBeGreaterThan(0));
    expect(subscriptionsOpened().at(-1)).toEqual({ types: ['Site'] });
    unmount();
  });

  // A navigation takes the leaving page's declaration back before the arriving page makes its own,
  // and the arriving page's code is fetched on demand, so the gap between the two is a load rather
  // than a tick. Between two pages that both read the whole model, acting inside it would build a
  // whole-model snapshot and re-read a whole model for a reader that never existed.
  it('reopens nothing when one page hands over to another asking for the same thing', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const leaving = mountPage({ all: true, ids: ['a'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));

    vi.useFakeTimers();
    leaving.unmount();
    await vi.advanceTimersByTimeAsync(100); // the arriving page's code is still loading
    const arriving = mountPage({ all: true, ids: ['a'] });
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();

    expect(objectStreams().length).toBe(2);
    arriving.unmount();
    unmount();
  });

  // A page replacing its own declaration — the scope switcher choosing another entity — has made
  // the new one before this turn is over. Waiting out the hand-over window there would leave the
  // page showing the entity it was about until the wait elapsed, for a hand-over that never
  // happened.
  it('follows a page that replaces its own declaration without waiting', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const showing = mountPage({ ids: ['a'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));

    showing.unmount();
    const showingAnother = mountPage({ ids: ['b'] });
    await act(async () => {}); // one turn, no timers advanced

    expect(objectStreams().length).toBe(3);
    expect(subscriptionsOpened().at(-1)).toEqual({ ids: ['b'] });
    showingAnother.unmount();
    unmount();
  });

  // Nothing expires a subscription (Bug #6562), and a page declaring its own opens one per
  // navigation, so an abandoned one would go on being written to for the life of the process.
  it('hands back the subscription it replaces', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ types: ['Site'] });
    await waitFor(() =>
      expect(vi.mocked(globalThis.fetch).mock.calls.some(
        ([url, init]) => (init as RequestInit | undefined)?.method === 'DELETE' && String(url).endsWith('/api/subscriptions/s1'),
      )).toBe(true));

    page.unmount();
    unmount();
  });

  // A declaration changing while an open is in flight abandons the subscription that open was
  // granted. Nothing on the platform expires one, and the streams it would have been released with
  // are never attached, so it has to be handed back on the way out.
  it('hands back a subscription the open that asked for it abandoned', async () => {
    let answer: (body: unknown) => void = () => {};
    globalThis.fetch = vi.fn().mockImplementation((_url, init?: RequestInit) => {
      if (init?.method === 'DELETE') return Promise.resolve({ ok: true });
      return new Promise((resolve) => {
        answer = (body) => resolve({ ok: true, json: async () => body } as Response);
      });
    }) as unknown as typeof fetch;

    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(answer).not.toBe(undefined));
    const page = mountPage({ types: ['Site'] });          // supersedes the open in flight
    await act(async () => { answer({ subscriptionId: 'abandoned', watermark: 0 }); });

    await waitFor(() =>
      expect(vi.mocked(globalThis.fetch).mock.calls.some(
        ([url, init]) => (init as RequestInit | undefined)?.method === 'DELETE'
          && String(url).endsWith('/api/subscriptions/abandoned'),
      )).toBe(true));
    page.unmount();
    unmount();
  });

  // A refused request leaves no subscription behind, so there is nothing to hand back — and the
  // page still has no data, so the open has to be retried. The refusal is what schedules the retry,
  // so the clock is faked before the render that provokes it.
  it('retries a refused subscription request and hands nothing back', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 503 }) as unknown as typeof fetch;
    vi.useFakeTimers();
    const { unmount } = renderHook(() => useSse());
    await vi.advanceTimersByTimeAsync(0);    // the refusal lands and schedules the retry
    expect(subscriptionsOpened().length).toBe(1);

    await vi.advanceTimersByTimeAsync(1000); // first backoff delay
    vi.useRealTimers();

    expect(subscriptionsOpened().length).toBe(2);
    expect(vi.mocked(globalThis.fetch).mock.calls
      .some(([, init]) => (init as RequestInit | undefined)?.method === 'DELETE')).toBe(false);
    unmount();
  });

  // A replaced model holds none of the Things the subscription resolved to, so it is asked for
  // again — once, even when a declaration was already on its way to being followed.
  it('asks for the subscription again, and only once when a change was already settling', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    vi.useFakeTimers();
    const leaving = mountPage({ types: ['Site'] });
    leaving.unmount();          // starts the settle window
    resubscribe();
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(objectStreams().length).toBe(2);
    unmount();
  });

  /** A platform answering with a snapshot of one Thing and the edge it sits on. */
  function answersWithASnapshot() {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        subscriptionId: 's1',
        watermark: 4,
        snapshot: {
          things: [{ Id: 't1', Name: 'Site', Properties: { area: { typeInfo: 'vos.Double', value: 3 } } }],
          relationships: [{ Id: 'r1', SubjectId: 't1', PredicateId: 'p', TargetId: 't2', Properties: {} }],
        },
      }),
    }) as unknown as typeof fetch;
  }

  // A subscription answers with the objects it covers and then streams the changes to them. A
  // reader given that before the stream was listening would apply it and then miss everything
  // between the two.
  it('hands what a narrowed subscription covers to the handlers once the streams are attached', async () => {
    answersWithASnapshot();
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(SUBSCRIPTION_OPENED, handler); });
    const page = mountPage({ types: ['Site'] });

    await waitFor(() => expect(handler).toHaveBeenCalled());
    const opened = handler.mock.calls[0][0] as SubscriptionOpened;
    expect(opened.watermark).toBe(4);
    expect(opened.covered!.things[0].Properties).toEqual({ area: 3 }); // unwrapped, as read elsewhere
    expect(opened.covered!.relationships).toHaveLength(1);
    act(() => off());
    page.unmount();
    unmount();
  });

  // The model read is what fills the store for a whole-model page, because only it honours the
  // properties the model says its pages are drawn with. Converting the snapshot into the shape the
  // store holds and then discarding it costs an object per Thing and per property on the largest
  // answer the platform gives.
  it('leaves a whole-model subscription\'s snapshot unread', async () => {
    answersWithASnapshot();
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(SUBSCRIPTION_OPENED, handler); });

    await waitFor(() => expect(handler).toHaveBeenCalled());
    expect((handler.mock.calls[0][0] as SubscriptionOpened).covered).toBeNull();
    act(() => off());
    unmount();
  });

  // The consumed position belongs to the coverage it was consumed under. Replaying from it against
  // different coverage re-applies what the new snapshot already holds, and can ask the broker for a
  // range it no longer retains.
  it('resumes a newly declared subscription from its own snapshot, not the consumed position', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].emit('ThingCreated', { EntityId: 't7' }, 7));

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ subscriptionId: 's2', watermark: 12 }),
    }) as unknown as typeof fetch;
    const page = mountPage({ types: ['Site'] });

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(objectStreams()[1].url).toContain('lastEventId=12');
    page.unmount();
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
