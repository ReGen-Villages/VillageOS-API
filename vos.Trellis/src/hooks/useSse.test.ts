import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  SUBSCRIPTION_CHANGING, SUBSCRIPTION_OPENED, addToSubscription, removeFromSubscription, resubscribe, useSse, useSubscription,
  useDefaultSubscription,
} from './useSse';
import { apiClient } from '../api/client';
import type { SubscriptionSelector, SubscriptionOpened } from '../types/subscription';

// Records each EventSource and its listeners, so a test can send events as the server would.
class FakeEventSource {
  static instances: FakeEventSource[] = [];
  url: string;
  listeners = new Map<string, (e: MessageEvent) => void>();
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  constructor(url: string) { this.url = url; FakeEventSource.instances.push(this); }
  addEventListener(type: string, callback: (e: MessageEvent) => void) { this.listeners.set(type, callback); }
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

// Every kind the platform publishes on its system events stream.
const SYSTEM_STREAM_KINDS = [
  'ActivityEvent', 'DaemonStatusChanged', 'EndpointServiceRequestCompleted',
  'EngineConfigurationChanged', 'ServiceHealthChanged',
] as const;

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

  it.each(['PropertyChanged', 'PropertyObserved'] as const)('maps a %s event to positional args', async (kind) => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(kind, handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
    act(() => FakeEventSource.instances[0].emit(kind,
      { EntityId: 't1', PropertyName: 'temp', Value: 5 }));

    expect(handler).toHaveBeenCalledWith('t1', 'temp', 5);
    act(() => off());
    unmount();
  });

  // A property event missing from that set reaches its handler as a raw object, and the change never
  // lands, with no error.
  it.each([
    ['PropertyChanged', { EntityId: 't1', PropertyName: 'temp', Value: 5 }, ['t1', 'temp', 5]],
    ['PropertyDeleted', { EntityId: 't1', PropertyName: 'temp' }, ['t1', 'temp', undefined]],
    ['RelationshipPropertyChanged', { EntityId: 'r1', PropertyName: 'total', Value: 9 }, ['r1', 'total', 9]],
    ['RelationshipPropertyDeleted', { EntityId: 'r1', PropertyName: 'total' }, ['r1', 'total', undefined]],
  ] as const)('maps %s to positional args', async (event, payload, expected) => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(event, handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
    act(() => FakeEventSource.instances[0].emit(event, payload));

    expect(handler).toHaveBeenCalledWith(...expected);
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

  it.each(SYSTEM_STREAM_KINDS)('hands a %s event from the system stream to its handler', async (kind) => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(kind, handler); });

    await waitFor(() => expect(FakeEventSource.instances.length).toBe(2));
    const systemStream = FakeEventSource.instances.find((source) => source.url.includes('/api/events/stream'))!;
    act(() => systemStream.emit(kind, { Engine: 'ranges' }));

    expect(handler).toHaveBeenCalledWith({ Engine: 'ranges' });
    act(() => off());
    unmount();
  });

  it.each(['ThingEntered', 'ThingLeft', 'RelationshipEntered', 'RelationshipLeft'] as const)(
    'hands a %s event through as the object the platform sent', async (kind) => {
      const { result, unmount } = renderHook(() => useSse());
      const handler = vi.fn();
      let off: () => void = () => {};
      act(() => { off = result.current.on(kind, handler); });

      await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
      act(() => FakeEventSource.instances[0].emit(kind, { EntityId: 't1', Thing: { Id: 't1' } }));

      expect(handler).toHaveBeenCalledWith({ EntityId: 't1', Thing: { Id: 't1' } });
      act(() => off());
      unmount();
    },
  );

  it('opens both the object subscription and the system-events stream', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(FakeEventSource.instances.length).toBe(2));

    const urls = FakeEventSource.instances.map((e) => e.url);
    expect(urls.some((u) => u.includes('/api/subscriptions/s1/stream'))).toBe(true);
    expect(urls.some((u) => u.includes('/api/events/stream'))).toBe(true);
    expect(urls.every((u) => u.includes('access_token=stream-token'))).toBe(true);
    unmount();
  });

  // The address is recorded in access logs, proxies and history, so only the short-lived token may go in it.
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

  it('resumes the object stream from the consumed sequence on reconnect', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    expect(objectStreams()[0].url).toContain('lastEventId=0');

    const obj0 = objectStreams()[0];
    act(() => obj0.emit('ThingCreated', { EntityId: 't7' }, 7));

    vi.useFakeTimers();
    act(() => obj0.onerror?.());
    await vi.advanceTimersByTimeAsync(1000);   // the first wait before reconnecting
    vi.useRealTimers();

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(objectStreams()[1].url).toContain('lastEventId=7');
    unmount();
  });

  it('is connecting until the object stream opens, then live', async () => {
    const { result, unmount } = renderHook(() => useSse());
    expect(result.current.connection).toBe('connecting');

    await waitFor(() => expect(objectStreams().length).toBe(1));
    expect(result.current.connection).toBe('connecting');

    act(() => objectStreams()[0].onopen?.());
    expect(result.current.connection).toBe('live');
    unmount();
  });

  it('is lost from the moment a stream fails until one opens again', async () => {
    const { result, unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].onopen?.());

    vi.useFakeTimers();
    act(() => objectStreams()[0].onerror?.());
    expect(result.current.connection).toBe('lost');
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();

    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(result.current.connection).toBe('lost');
    act(() => objectStreams()[1].onopen?.());
    expect(result.current.connection).toBe('live');
    unmount();
  });

  // A page changing what the subscription covers closes the streams on purpose. Nothing is
  // arriving while the new ones open, and nothing has gone wrong.
  it('is connecting, not live or lost, while a changed declaration reopens the streams', async () => {
    const { result, unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].onopen?.());

    const page = mountPage({ types: ['Site'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(result.current.connection).toBe('connecting');

    act(() => objectStreams()[1].onopen?.());
    expect(result.current.connection).toBe('live');
    page.unmount();
    unmount();
  });

  it('starts from connecting again once the last reader has left', async () => {
    const first = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].onopen?.());
    first.unmount();

    const second = renderHook(() => useSse());
    expect(second.result.current.connection).toBe('connecting');
    second.unmount();
  });

  it('announces a change of coverage before it asks the platform for the subscription', async () => {
    const { result, unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const requestsWhenAnnounced: number[] = [];
    const announced: unknown[] = [];
    let off: () => void = () => {};
    act(() => {
      off = result.current.on(SUBSCRIPTION_CHANGING, (selector) => {
        announced.push(selector);
        requestsWhenAnnounced.push(subscriptionsOpened().length);
      });
    });

    const page = mountPage({ types: ['Site'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));

    expect(announced).toEqual([{ types: ['Site'] }]);
    expect(requestsWhenAnnounced).toEqual([1]);
    act(() => off());
    page.unmount();
    unmount();
  });

  it('announces no change of coverage for a reconnect', async () => {
    const { result, unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const announced = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(SUBSCRIPTION_CHANGING, announced); });

    vi.useFakeTimers();
    act(() => objectStreams()[0].onerror?.());
    await vi.advanceTimersByTimeAsync(1000);
    vi.useRealTimers();
    await waitFor(() => expect(objectStreams().length).toBe(2));

    expect(announced).not.toHaveBeenCalled();
    act(() => off());
    unmount();
  });

  const subscriptionsOpened = () =>
    vi.mocked(globalThis.fetch).mock.calls
      .filter(([, requestOptions]) => (requestOptions as RequestInit | undefined)?.method === 'POST')
      .map(([, requestOptions]) => JSON.parse((requestOptions as RequestInit).body as string));

  /** Unmounting it is the page leaving. */
  const mountPage = (selector: SubscriptionSelector) => renderHook(() => useSubscription(selector));

  it('opens the subscription the mounted page declared, and restores the last one when it leaves', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ types: ['Site'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));
    expect(subscriptionsOpened()[1]).toEqual({ types: ['Site'] });

    page.unmount();
    await waitFor(() => expect(objectStreams().length).toBe(3));
    expect(subscriptionsOpened()[2]).toEqual({ all: true, includeSnapshot: false });
    unmount();
  });

  // Reopening costs a snapshot or a whole-model read, so a page drawn again, or declaring what the page
  // before it did, must not cause one.
  it('reopens nothing when a declaration says what is already open', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ all: true });
    await act(async () => {});

    expect(objectStreams().length).toBe(1);
    page.unmount();
    unmount();
  });

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

  // The arriving page's code is fetched when needed, so the gap between two pages lasts a load, not a moment.
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

  // Waiting here would leave the page showing the entity it was about until the wait ended.
  it('follows a page that replaces its own declaration without waiting', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    const showing = mountPage({ ids: ['a'] });
    await waitFor(() => expect(objectStreams().length).toBe(2));

    showing.unmount();
    const showingAnother = mountPage({ ids: ['b'] });
    await act(async () => {});

    expect(objectStreams().length).toBe(3);
    expect(subscriptionsOpened().at(-1)).toEqual({ ids: ['b'] });
    showingAnother.unmount();
    unmount();
  });

  it('hands back the subscription it replaces', async () => {
    const { unmount } = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));

    const page = mountPage({ types: ['Site'] });
    await waitFor(() =>
      expect(vi.mocked(globalThis.fetch).mock.calls.some(
        ([url, requestOptions]) => (requestOptions as RequestInit | undefined)?.method === 'DELETE' && String(url).endsWith('/api/subscriptions/s1'),
      )).toBe(true));

    page.unmount();
    unmount();
  });

  it('hands back a subscription the open that asked for it abandoned', async () => {
    let answer: (body: unknown) => void = () => {};
    globalThis.fetch = vi.fn().mockImplementation((_url, requestOptions?: RequestInit) => {
      if (requestOptions?.method === 'DELETE') return Promise.resolve({ ok: true });
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
        ([url, requestOptions]) => (requestOptions as RequestInit | undefined)?.method === 'DELETE'
          && String(url).endsWith('/api/subscriptions/abandoned'),
      )).toBe(true));
    page.unmount();
    unmount();
  });

  // The refusal is what schedules the retry, so the clock is faked before the render that causes it.
  it('retries a refused subscription request and hands nothing back', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 503 }) as unknown as typeof fetch;
    vi.useFakeTimers();
    const { unmount } = renderHook(() => useSse());
    await vi.advanceTimersByTimeAsync(0);
    expect(subscriptionsOpened().length).toBe(1);

    await vi.advanceTimersByTimeAsync(1000); // the first wait before retrying
    vi.useRealTimers();

    expect(subscriptionsOpened().length).toBe(2);
    expect(vi.mocked(globalThis.fetch).mock.calls
      .some(([, requestOptions]) => (requestOptions as RequestInit | undefined)?.method === 'DELETE')).toBe(false);
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

  it('hands what a narrowed subscription covers to the handlers once the streams are attached', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        subscriptionId: 's1',
        watermark: 4,
        snapshot: {
          things: [
            { Id: 't1', Name: 'Site', Properties: { area: { typeInfo: 'vos.Double', value: 3 } }, States: ['flagged'] },
            { Id: 't2', Name: 'Spring', Properties: {}, States: [] },
          ],
          relationships: [{ Id: 'r1', SubjectId: 't1', PredicateId: 'p', TargetId: 't2', Properties: {} }],
        },
      }),
    }) as unknown as typeof fetch;
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
    expect(opened.covered!.thingStates).toEqual(new Map([['t1', ['flagged']], ['t2', []]]));
    act(() => off());
    page.unmount();
    unmount();
  });

  it('asks for no snapshot when the subscription covers the whole model', async () => {
    const { result, unmount } = renderHook(() => useSse());
    const handler = vi.fn();
    let off: () => void = () => {};
    act(() => { off = result.current.on(SUBSCRIPTION_OPENED, handler); });

    await waitFor(() => expect(handler).toHaveBeenCalled());
    expect(subscriptionsOpened()[0]).toEqual({ all: true, includeSnapshot: false });
    expect((handler.mock.calls[0][0] as SubscriptionOpened).covered).toBeNull();
    act(() => off());
    unmount();
  });

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

  describe('adding to the open subscription', () => {
    const requests = () => vi.mocked(globalThis.fetch).mock.calls
      .map(([address, requestOptions]) => ({
        address: String(address),
        method: (requestOptions as RequestInit).method,
        body: JSON.parse((requestOptions as RequestInit).body as string),
      }))
      .filter((request) => request.address.endsWith('/objects'));

    it('asks the open subscription for the objects and answers with them as the store holds them', async () => {
      const { unmount } = renderHook(() => useSse());
      await waitFor(() => expect(objectStreams().length).toBe(1));
      vi.mocked(globalThis.fetch).mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          watermark: 9,
          snapshot: {
            things: [{ Id: 'k1', Name: 'Strategy', Properties: { rank: { typeInfo: 'vos.Int32', value: 2 } }, States: ['current'] }],
            relationships: [{ Id: 'r1', SubjectId: 'k1', PredicateId: 'p', TargetId: 'k2', Properties: {} }],
          },
        }),
      } as Response);

      const covered = await addToSubscription({ typeIds: ['k1'] });

      expect(requests()).toEqual([{ address: '/api/subscriptions/s1/objects', method: 'POST', body: { typeIds: ['k1'] } }]);
      expect(covered!.things).toEqual([{ Id: 'k1', Name: 'Strategy', Properties: { rank: 2 }, States: ['current'] }]);
      expect(covered!.relationships).toHaveLength(1);
      expect(covered!.thingStates).toEqual(new Map([['k1', ['current']]]));
      unmount();
    });

    it('answers null while no subscription is open, and asks the platform for nothing', async () => {
      expect(await addToSubscription({ typeIds: ['k1'] })).toBeNull();
      expect(requests()).toEqual([]);
    });

    it('reports a refused addition', async () => {
      const { unmount } = renderHook(() => useSse());
      await waitFor(() => expect(objectStreams().length).toBe(1));
      vi.mocked(globalThis.fetch).mockResolvedValueOnce({ ok: false, status: 404 } as Response);

      await expect(addToSubscription({ typeIds: ['k1'] })).rejects.toThrow('404');
      unmount();
    });

    it('takes the objects out of the open subscription, and asks nothing for an empty list or no subscription', async () => {
      await removeFromSubscription(['k1']);
      expect(requests()).toEqual([]);

      const { unmount } = renderHook(() => useSse());
      await waitFor(() => expect(objectStreams().length).toBe(1));
      await removeFromSubscription([]);
      expect(requests()).toEqual([]);

      await removeFromSubscription(['k1', 'r1']);
      expect(requests()).toEqual([{ address: '/api/subscriptions/s1/objects', method: 'DELETE', body: { ids: ['k1', 'r1'] } }]);

      vi.mocked(globalThis.fetch).mockResolvedValueOnce({ ok: false, status: 404 } as Response);
      await expect(removeFromSubscription(['k1'])).rejects.toThrow('404');
      unmount();
    });
  });

  it('resets the watermark on full teardown so a model switch restarts from the fresh snapshot', async () => {
    const first = renderHook(() => useSse());
    await waitFor(() => expect(objectStreams().length).toBe(1));
    act(() => objectStreams()[0].emit('ThingCreated', { EntityId: 't7' }, 7));
    first.unmount();

    // Another model's snapshot is at 99, so the remount resumes from 99, not the old 7.
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

  describe('a stream that goes quiet', () => {
    const TWO_HEARTBEATS = 30_000;
    const systemStream = () => FakeEventSource.instances.find((e) => e.url.includes('/api/events/stream'))!;

    afterEach(() => vi.useRealTimers());

    async function liveUnderAMovableClock() {
      vi.useFakeTimers();
      const hook = renderHook(() => useSse());
      for (let turn = 0; turn < 10 && FakeEventSource.instances.length < 2; turn += 1)
        await act(async () => { await vi.advanceTimersByTimeAsync(0); });
      act(() => objectStreams()[0].onopen?.());
      expect(hook.result.current.connection).toBe('live');
      return hook;
    }

    const pass = (milliseconds: number) => act(async () => { await vi.advanceTimersByTimeAsync(milliseconds); });

    it('asks each stream for its heartbeat as an event', async () => {
      const { unmount } = await liveUnderAMovableClock();

      expect(FakeEventSource.instances.every((stream) => stream.url.includes('heartbeatAsEvent=true'))).toBe(true);
      unmount();
    });

    it('is lost, and reopens, when the streams send nothing for longer than two heartbeats', async () => {
      const { result, unmount } = await liveUnderAMovableClock();

      await pass(TWO_HEARTBEATS);
      expect(result.current.connection).toBe('lost');

      await pass(1000);
      expect(objectStreams().length).toBe(2);
      expect(objectStreams()[0].closed).toBe(true);
      unmount();
    });

    it('stays live while each stream sends a heartbeat within every two', async () => {
      const { result, unmount } = await liveUnderAMovableClock();

      for (let heard = 0; heard < 4; heard += 1) {
        await pass(TWO_HEARTBEATS - 1000);
        act(() => {
          objectStreams()[0].emit('Heartbeat', {});
          systemStream().emit('Heartbeat', {});
        });
      }

      expect(result.current.connection).toBe('live');
      expect(objectStreams().length).toBe(1);
      unmount();
    });

    it('takes an event the page listens for as a sign of life', async () => {
      const { result, unmount } = await liveUnderAMovableClock();

      for (let heard = 0; heard < 4; heard += 1) {
        await pass(TWO_HEARTBEATS - 1000);
        act(() => {
          objectStreams()[0].emit('ThingCreated', { EntityId: 't1' });
          systemStream().emit('ActivityEvent', { Type: 'X' });
        });
      }

      expect(result.current.connection).toBe('live');
      unmount();
    });

    it('is lost when one stream goes quiet while the other keeps sending', async () => {
      const { result, unmount } = await liveUnderAMovableClock();

      await pass(TWO_HEARTBEATS - 1000);
      act(() => objectStreams()[0].emit('Heartbeat', {}));
      await pass(1000);

      expect(result.current.connection).toBe('lost');
      unmount();
    });

    it('stops waiting on a stream it has closed', async () => {
      const { result, unmount } = await liveUnderAMovableClock();

      await pass(TWO_HEARTBEATS - 1000);
      act(() => resubscribe());
      for (let turn = 0; turn < 10 && objectStreams().length < 2; turn += 1) await pass(0);
      act(() => objectStreams()[1].onopen?.());
      await pass(2000);

      expect(result.current.connection).toBe('live');
      expect(objectStreams().length).toBe(2);
      unmount();
    });
  });
});
