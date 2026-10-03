import { useEffect, useRef, useState } from 'react';
import { apiClient } from '../api/client';
import { connectionParameter, fetchLatestRequests, mergeNewestFirst, type RequestLogEntry } from '../api/requestLogApi';
import type { ConnectionState } from '../types/connection';
import { HEARTBEAT_AS_EVENT, watchForSilence, type SilenceWatch } from './streamSilence';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/** The page's own limit: the oldest drop off as new ones arrive, since every arrival redraws the list. */
const ENTRIES_HELD = 1000;

const RECONNECT_DELAYS_MILLISECONDS = [1000, 2000, 5000, 10000, 30000];

/**
 * The broker's request log, newest first: the latest entries of the last day, then each one as it is
 * recorded. Narrowed to one connection where one is named. The stream replays the newest entries when it
 * opens and again on every reconnect, so an entry is kept once by its identifier.
 */
export function useRequestLog(connection: string | undefined): { entries: RequestLogEntry[]; streamState: ConnectionState } {
  const [entries, setEntries] = useState<RequestLogEntry[]>([]);
  const [streamState, setStreamState] = useState<ConnectionState>('connecting');

  const sourceReference = useRef<EventSource | null>(null);
  const silenceReference = useRef<SilenceWatch | null>(null);
  const reconnectReference = useRef<ReturnType<typeof setTimeout> | null>(null);
  const attemptReference = useRef(0);

  useEffect(() => {
    let released = false;
    const take = (arrived: RequestLogEntry[]) => {
      if (!released) setEntries((held) => mergeNewestFirst(held, arrived, ENTRIES_HELD));
    };

    fetchLatestRequests(connection).then(take, () => {});

    const scheduleReconnect = () => {
      if (reconnectReference.current) return;
      setStreamState('lost');
      const delay = RECONNECT_DELAYS_MILLISECONDS[Math.min(attemptReference.current, RECONNECT_DELAYS_MILLISECONDS.length - 1)];
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
        const narrowing = connectionParameter(connection);
        const url = `${BASE_URL}/api/requests/stream?${narrowing ? `${narrowing}&` : ''}access_token=${encodeURIComponent(streamToken)}&${HEARTBEAT_AS_EVENT}`;
        const source = new EventSource(url);
        const lost = () => {
          silence.stop();
          source.close();
          scheduleReconnect();
        };
        const silence = watchForSilence(source, lost);
        silenceReference.current = silence;
        source.onopen = () => {
          attemptReference.current = 0;
          setStreamState('live');
        };
        source.onerror = lost;
        source.addEventListener('request', (event: MessageEvent) => {
          silence.heard();
          try {
            take([JSON.parse(event.data) as RequestLogEntry]);
          } catch {
            // A line the broker could not have written whole is passed over, as the broker's own reader does.
          }
        });
        sourceReference.current = source;
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
    // The page keys this hook's view by connection, so a change of connection remounts it rather than
    // re-running this effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { entries, streamState };
}
