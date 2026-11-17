import { useEffect, useRef, useState } from 'react';
import { apiClient } from '../api/client';
import type { ConnectionState } from '../types/connection';
import { HEARTBEAT_AS_EVENT, watchForSilence, type SilenceWatch } from './streamSilence';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

const RECONNECT_DELAYS_MILLISECONDS = [1000, 2000, 5000, 10000, 30000];

/**
 * Follows one of the broker's event streams and hands each message of `event` to `onMessage`.
 *
 * EventSource cannot set an Authorization header, so each open carries a stream token minted for it.
 * The stream reopens after an error, or after a silence longer than the heartbeat allows, waiting longer
 * after each failed try, and closes when the view goes. `path` and `onMessage` are read once, when the view mounts: the pages
 * key these views by what they follow, so following something else remounts the view.
 */
export function useBrokerStream(path: string, event: string, onMessage: (data: string) => void): ConnectionState {
  const [state, setState] = useState<ConnectionState>('connecting');

  const sourceReference = useRef<EventSource | null>(null);
  const silenceReference = useRef<SilenceWatch | null>(null);
  const reconnectReference = useRef<ReturnType<typeof setTimeout> | null>(null);
  const attemptReference = useRef(0);

  useEffect(() => {
    let released = false;

    const scheduleReconnect = () => {
      if (reconnectReference.current) return;
      setState('lost');
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
        const separator = path.includes('?') ? '&' : '?';
        const source = new EventSource(
          `${BASE_URL}${path}${separator}access_token=${encodeURIComponent(streamToken)}&${HEARTBEAT_AS_EVENT}`,
        );
        const lost = () => {
          silence.stop();
          source.close();
          scheduleReconnect();
        };
        const silence = watchForSilence(source, lost);
        silenceReference.current = silence;
        source.onopen = () => {
          attemptReference.current = 0;
          setState('live');
        };
        source.onerror = lost;
        source.addEventListener(event, (message: MessageEvent) => {
          silence.heard();
          onMessage(message.data);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return state;
}
