import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useLogTail } from './useLogTail';
import { apiClient } from '../api/client';

// Stands in for the browser's EventSource so a test can open, feed and break the stream.
class FakeEventSource {
  static instances: FakeEventSource[] = [];

  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;

  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();

  constructor(public readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close() {
    this.closed = true;
  }

  emit(type: string, data: string) {
    for (const listener of this.listeners.get(type) ?? [])
      listener(new MessageEvent(type, { data }));
  }

  static latest(): FakeEventSource {
    return FakeEventSource.instances[FakeEventSource.instances.length - 1];
  }

  static reset() {
    FakeEventSource.instances = [];
  }
}

// Under fake timers the token promise only settles when the queue is drained by hand.
async function flushUntilOpened(): Promise<FakeEventSource> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
  return FakeEventSource.latest();
}

async function openedStream(): Promise<FakeEventSource> {
  await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
  return FakeEventSource.latest();
}

describe('useLogTail', () => {
  beforeEach(() => {
    FakeEventSource.reset();
    vi.stubGlobal('EventSource', FakeEventSource);
    vi.spyOn(apiClient, 'ensureToken').mockResolvedValue('a-short-lived-token');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // EventSource cannot send an Authorization header, so the token rides on the query string.
  it('opens the broker log with the tail length and the access token', async () => {
    renderHook(() => useLogTail());

    const stream = await openedStream();
    expect(stream.url).toContain('/api/logs/stream');
    expect(stream.url).toContain('tail=200');
    expect(stream.url).toContain('access_token=a-short-lived-token');
    expect(stream.url).not.toContain('service=');
  });

  it('names the service when watching a daemon log', async () => {
    renderHook(() => useLogTail('water reserve'));

    expect((await openedStream()).url).toContain('service=water%20reserve');
  });

  it('reports the stream as connected once it opens', async () => {
    const { result } = renderHook(() => useLogTail());
    const stream = await openedStream();

    expect(result.current.connected).toBe(false);
    act(() => stream.onopen?.());

    expect(result.current.connected).toBe(true);
  });

  it('collects the lines the broker sends, in order', async () => {
    const { result } = renderHook(() => useLogTail());
    const stream = await openedStream();

    act(() => {
      stream.emit('log', JSON.stringify('first line'));
      stream.emit('log', JSON.stringify('second line'));
    });

    expect(result.current.lines).toEqual(['first line', 'second line']);
  });

  // The broker sends each line as a JSON string; anything else is taken as the line itself rather
  // than dropped, so a malformed frame still shows up in the view.
  it('keeps a line that is not valid JSON', async () => {
    const { result } = renderHook(() => useLogTail());
    const stream = await openedStream();

    act(() => stream.emit('log', 'a bare line, not JSON'));

    expect(result.current.lines).toEqual(['a bare line, not JSON']);
  });

  it('empties the view when asked', async () => {
    const { result } = renderHook(() => useLogTail());
    const stream = await openedStream();

    act(() => stream.emit('log', JSON.stringify('a line')));
    expect(result.current.lines).toHaveLength(1);

    act(() => result.current.clear());
    expect(result.current.lines).toEqual([]);
  });

  it('reports the stream as disconnected when it breaks', async () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useLogTail());
    const stream = await flushUntilOpened();

    act(() => stream.onopen?.());
    expect(result.current.connected).toBe(true);

    act(() => stream.onerror?.());

    expect(result.current.connected).toBe(false);
    expect(stream.closed).toBe(true);
  });

  it('reopens the stream after a break', async () => {
    vi.useFakeTimers();
    renderHook(() => useLogTail());
    const stream = await flushUntilOpened();

    act(() => stream.onerror?.());
    expect(FakeEventSource.instances).toHaveLength(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });

    expect(FakeEventSource.instances.length).toBeGreaterThan(1);
  });

  it('closes the stream when the view goes away', async () => {
    const { unmount } = renderHook(() => useLogTail());
    const stream = await openedStream();

    unmount();

    expect(stream.closed).toBe(true);
  });

  // Without a token there is no stream to open, so the hook waits and tries again rather than
  // failing silently for the rest of the session.
  it('retries when no token can be obtained', async () => {
    vi.useFakeTimers();
    vi.spyOn(apiClient, 'ensureToken').mockRejectedValue(new Error('not signed in'));

    const { result } = renderHook(() => useLogTail());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(result.current.connected).toBe(false);
    expect(FakeEventSource.instances).toHaveLength(0);

    vi.spyOn(apiClient, 'ensureToken').mockResolvedValue('a-short-lived-token');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });

    expect(FakeEventSource.instances).toHaveLength(1);
  });
});
