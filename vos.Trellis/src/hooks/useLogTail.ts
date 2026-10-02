import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from '../api/client';
import { appendLines } from '../utils/logBuffer';
import type { ConnectionState } from '../types/connection';
import { HEARTBEAT_AS_EVENT, watchForSilence, type SilenceWatch } from './streamSilence';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

// How many trailing lines the stream replays before going live.
const TAIL_LINES = 200;

/**
 * Tails a log over SSE — the Mycelium broker log by default, or a named service daemon's log when
 * `service` is given (e.g. 'irrigator' → watch-irrigator.log). Mirrors useSse's authentication approach:
 * EventSource can't set an Authorization header, so the address carries a stream token minted for
 * this one open (the /api/logs/stream path is one of Mycelium's BrowserStreamPaths). Reconnects
 * with backoff on error or when the stream goes quiet, and re-opens against the new source when
 * `service` changes.
 */
export function useLogTail(service?: string): { lines: string[]; connection: ConnectionState; clear: () => void } {
  const [lines, setLines] = useState<string[]>([]);
  const [connection, setConnection] = useState<ConnectionState>('connecting');

  const sourceReference = useRef<EventSource | null>(null);
  const silenceReference = useRef<SilenceWatch | null>(null);
  const reconnectReference = useRef<ReturnType<typeof setTimeout> | null>(null);
  const attemptReference = useRef(0);

  const clear = useCallback(() => setLines([]), []);

  useEffect(() => {
    let released = false;

    const scheduleReconnect = () => {
      if (reconnectReference.current) return;
      setConnection('lost');
      const delays = [1000, 2000, 5000, 10000, 30000];
      const delay = delays[Math.min(attemptReference.current, delays.length - 1)];
      attemptReference.current += 1;
      reconnectReference.current = setTimeout(() => {
        reconnectReference.current = null;
        if (!released) void open();
      }, delay);
    };

    const open = async () => {
      sourceReference.current?.close();
      try {
        const streamToken = await apiClient.mintStreamToken();
        if (released) return;
        const serviceParameter = service ? `&service=${encodeURIComponent(service)}` : '';
        const url = `${BASE_URL}/api/logs/stream?tail=${TAIL_LINES}${serviceParameter}&access_token=${encodeURIComponent(streamToken)}&${HEARTBEAT_AS_EVENT}`;
        const es = new EventSource(url);
        const lost = () => {
          silence.stop();
          es.close();
          scheduleReconnect();
        };
        const silence = watchForSilence(es, lost);
        silenceReference.current = silence;
        es.onopen = () => {
          attemptReference.current = 0;
          setConnection('live');
        };
        es.onerror = lost;
        es.addEventListener('log', (e: MessageEvent) => {
          silence.heard();
          let line: string;
          try {
            line = JSON.parse(e.data) as string;
          } catch {
            line = e.data;
          }
          setLines((previous) => appendLines(previous, [line]));
        });
        sourceReference.current = es;
      } catch {
        scheduleReconnect();
      }
    };

    void open();

    return () => {
      released = true;
      if (reconnectReference.current) clearTimeout(reconnectReference.current);
      silenceReference.current?.stop();
      sourceReference.current?.close();
      sourceReference.current = null;
    };
    // `service` is fixed for this hook instance: LogPage keys the view by service, so a switch
    // remounts rather than re-running this effect. Opening once on mount is correct.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { lines, connection, clear };
}
