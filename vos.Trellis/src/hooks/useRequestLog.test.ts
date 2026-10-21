import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useRequestLog } from './useRequestLog';
import { apiClient } from '../api/client';
import * as requestLogApi from '../api/requestLogApi';
import type { RequestLogEntry } from '../api/requestLogApi';

class FakeEventSource {
  static instances: FakeEventSource[] = [];

  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;

  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();

  readonly url: string;

  constructor(url: string) {
    this.url = url;
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
}

function entry(id: string, time: string): RequestLogEntry {
  return {
    Id: id, Time: time, ModelId: 'm', ConnectionId: 'c-gauges', ConnectionName: 'reads the gauges',
    Caller: null, SubjectId: null, RelationshipId: null, Status: 200, DurationMilliseconds: 12, BodyBytes: 27, BodyKeptBytes: 0,
  };
}

async function openedStream(): Promise<FakeEventSource> {
  await waitFor(() => expect(FakeEventSource.instances.length).toBeGreaterThan(0));
  return FakeEventSource.latest();
}

describe('useRequestLog', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    vi.spyOn(apiClient, 'mintStreamToken').mockResolvedValue('a-stream-token');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('follows the request log with a stream token, narrowed to the connection given, with the heartbeat as an event', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);

    renderHook(() => useRequestLog('c-gauges'));

    const stream = await openedStream();
    expect(stream.url).toContain('/api/requests/stream?');
    expect(stream.url).toContain('connection=c-gauges');
    expect(stream.url).toContain('access_token=a-stream-token');
    expect(stream.url).toContain('heartbeatAsEvent=true');
    expect(requestLogApi.fetchLatestRequests).toHaveBeenCalledWith('c-gauges');
  });

  it('opens on every connection when none is named', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);

    renderHook(() => useRequestLog(undefined));

    expect((await openedStream()).url).not.toContain('connection=');
  });

  it('lists the latest entries and each one streamed after, newest first and once each', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([
      entry('r1', '2026-10-03T11:00:00+00:00'), entry('r2', '2026-10-03T11:01:00+00:00'),
    ]);
    const { result } = renderHook(() => useRequestLog(undefined));
    const stream = await openedStream();
    await waitFor(() => expect(result.current.entries).toHaveLength(2));

    act(() => {
      stream.emit('request', JSON.stringify(entry('r2', '2026-10-03T11:01:00+00:00')));
      stream.emit('request', JSON.stringify(entry('r3', '2026-10-03T11:02:00+00:00')));
    });

    expect(result.current.entries.map((e) => e.Id)).toEqual(['r3', 'r2', 'r1']);
  });

  it('reports the stream live once it opens, and lost then reopened when it breaks', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);
    const { result } = renderHook(() => useRequestLog(undefined));
    const first = await openedStream();

    act(() => first.onopen?.());
    expect(result.current.streamState).toBe('live');

    act(() => first.onerror?.());
    expect(result.current.streamState).toBe('lost');
    expect(first.closed).toBe(true);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(FakeEventSource.instances).toHaveLength(2);
  });

  it('reconnects once for two breaks before it retries, and retries when no stream token could be had', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);
    vi.mocked(apiClient.mintStreamToken).mockRejectedValueOnce(new Error('signed out'));
    const { result } = renderHook(() => useRequestLog(undefined));

    await waitFor(() => expect(result.current.streamState).toBe('lost'));
    expect(FakeEventSource.instances).toHaveLength(0);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    const opened = await openedStream();
    act(() => {
      opened.onerror?.();
      opened.onerror?.();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000);
    });

    expect(FakeEventSource.instances).toHaveLength(2);
  });

  it('opens nothing once the page has let go, whether a retry or a stream token was still on its way', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);
    let tokenArrives: (token: string) => void = () => {};
    vi.mocked(apiClient.mintStreamToken).mockReturnValueOnce(new Promise((resolve) => { tokenArrives = resolve; }));
    const late = renderHook(() => useRequestLog(undefined));
    late.unmount();
    await act(async () => tokenArrives('a-stream-token'));

    const retrying = renderHook(() => useRequestLog(undefined));
    const stream = await openedStream();
    act(() => stream.onerror?.());
    retrying.unmount();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000);
    });

    expect(FakeEventSource.instances).toEqual([stream]);
  });

  it('keeps streaming when the latest entries cannot be read', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockRejectedValue(new Error('refused'));
    const { result } = renderHook(() => useRequestLog(undefined));
    const stream = await openedStream();

    act(() => stream.emit('request', JSON.stringify(entry('r1', '2026-10-03T11:00:00+00:00'))));

    expect(result.current.entries.map((e) => e.Id)).toEqual(['r1']);
  });

  it('passes over a line that is not an entry and keeps the ones around it', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);
    const { result } = renderHook(() => useRequestLog(undefined));
    const stream = await openedStream();

    act(() => {
      stream.emit('request', '{"Id":"half-written');
      stream.emit('request', JSON.stringify(entry('r1', '2026-10-03T11:00:00+00:00')));
    });

    expect(result.current.entries.map((e) => e.Id)).toEqual(['r1']);
  });

  it('closes the stream when the page lets go of it', async () => {
    vi.spyOn(requestLogApi, 'fetchLatestRequests').mockResolvedValue([]);
    const { unmount } = renderHook(() => useRequestLog(undefined));
    const stream = await openedStream();

    unmount();

    expect(stream.closed).toBe(true);
  });
});
