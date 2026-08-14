import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from '../api/client';
import { appendLines } from '../utils/logBuffer';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

// How many trailing lines the stream replays before going live.
const TAIL_LINES = 200;

/**
 * Tails a log over SSE — the Mycelium broker log by default, or a named service daemon's log when
 * `service` is given (e.g. 'irrigator' → watch-irrigator.log). Mirrors useSse's auth approach:
 * EventSource can't set an Authorization header, so the address carries a stream token minted for
 * this one open (the /api/logs/stream path is one of Mycelium's BrowserStreamPaths). Reconnects
 * with backoff on error, and re-opens against the new source when `service` changes.
 */
export function useLogTail(service?: string): { lines: string[]; connected: boolean; clear: () => void } {
  const [lines, setLines] = useState<string[]>([]);
  const [connected, setConnected] = useState(false);

  const sourceRef = useRef<EventSource | null>(null);
  const reconnectRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const attemptRef = useRef(0);

  const clear = useCallback(() => setLines([]), []);

  useEffect(() => {
    let released = false;

    const scheduleReconnect = () => {
      if (reconnectRef.current) return;
      setConnected(false);
      const delays = [1000, 2000, 5000, 10000, 30000];
      const delay = delays[Math.min(attemptRef.current, delays.length - 1)];
      attemptRef.current += 1;
      reconnectRef.current = setTimeout(() => {
        reconnectRef.current = null;
        if (!released) void open();
      }, delay);
    };

    const open = async () => {
      sourceRef.current?.close();
      try {
        const streamToken = await apiClient.mintStreamToken();
        if (released) return;
        const serviceParam = service ? `&service=${encodeURIComponent(service)}` : '';
        const url = `${BASE_URL}/api/logs/stream?tail=${TAIL_LINES}${serviceParam}&access_token=${encodeURIComponent(streamToken)}`;
        const es = new EventSource(url);
        es.onopen = () => {
          attemptRef.current = 0;
          setConnected(true);
        };
        es.onerror = () => {
          es.close();
          scheduleReconnect();
        };
        es.addEventListener('log', (e: MessageEvent) => {
          let line: string;
          try {
            line = JSON.parse(e.data) as string;
          } catch {
            line = e.data;
          }
          setLines((prev) => appendLines(prev, [line]));
        });
        sourceRef.current = es;
      } catch {
        scheduleReconnect();
      }
    };

    void open();

    return () => {
      released = true;
      if (reconnectRef.current) clearTimeout(reconnectRef.current);
      sourceRef.current?.close();
      sourceRef.current = null;
    };
    // `service` is fixed for this hook instance: LogPage keys the view by service, so a switch
    // remounts rather than re-running this effect. Opening once on mount is correct.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { lines, connected, clear };
}
