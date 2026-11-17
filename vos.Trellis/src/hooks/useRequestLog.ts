import { useCallback, useEffect, useState } from 'react';
import { connectionParameter, fetchLatestRequests, mergeNewestFirst, type RequestLogEntry } from '../api/requestLogApi';
import type { ConnectionState } from '../types/connection';
import { useBrokerStream } from './useBrokerStream';

/** The page's own limit: the oldest drop off as new ones arrive, since every arrival redraws the list. */
const ENTRIES_HELD = 1000;

/**
 * The broker's request log, newest first: the latest entries of the last day, then each one as it is
 * recorded. Narrowed to one connection where one is named. The stream replays the newest entries when it
 * opens and again on every reconnect, so an entry is kept once by its identifier.
 */
export function useRequestLog(connection: string | undefined): { entries: RequestLogEntry[]; streamState: ConnectionState } {
  const [entries, setEntries] = useState<RequestLogEntry[]>([]);
  const take = useCallback(
    (arrived: RequestLogEntry[]) => setEntries((held) => mergeNewestFirst(held, arrived, ENTRIES_HELD)),
    [],
  );

  useEffect(() => {
    let released = false;
    fetchLatestRequests(connection).then((arrived) => { if (!released) take(arrived); }, () => {});
    return () => { released = true; };
    // The page keys this hook's view by connection, so a change of connection remounts it rather than
    // re-running this effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const narrowing = connectionParameter(connection);
  const streamState = useBrokerStream(`/api/requests/stream${narrowing ? `?${narrowing}` : ''}`, 'request', (data) => {
    try {
      take([JSON.parse(data) as RequestLogEntry]);
    } catch {
      // A line the broker could not have written whole is passed over, as the broker's own reader does.
    }
  });

  return { entries, streamState };
}
