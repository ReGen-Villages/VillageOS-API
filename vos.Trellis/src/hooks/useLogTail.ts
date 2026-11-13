import { useCallback, useState } from 'react';
import { appendLines } from '../utils/logBuffer';
import type { ConnectionState } from '../types/connection';
import { useBrokerStream } from './useBrokerStream';

// How many trailing lines the stream replays before going live.
const TAIL_LINES = 200;

/**
 * Tails a log over SSE — the Mycelium broker log by default, or a service's own log when `service`
 * names it by connection id or name. The /api/logs/stream path is one of Mycelium's BrowserStreamPaths,
 * so it accepts the stream token in its address.
 */
export function useLogTail(service?: string): { lines: string[]; connection: ConnectionState; clear: () => void } {
  const [lines, setLines] = useState<string[]>([]);
  const clear = useCallback(() => setLines([]), []);

  const serviceParameter = service ? `&service=${encodeURIComponent(service)}` : '';
  const connection = useBrokerStream(`/api/logs/stream?tail=${TAIL_LINES}${serviceParameter}`, 'log', (data) => {
    let line: string;
    try {
      line = JSON.parse(data) as string;
    } catch {
      line = data;
    }
    setLines((previous) => appendLines(previous, [line]));
  });

  return { lines, connection, clear };
}
