import { apiClient, ApiError } from './client';
import { fileNameFromContentDisposition } from '../utils/logDownload';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/** One request the broker passed to a service, as its request log records it outside the model. The
 *  connection's name is the one it had when the request was made. */
export interface RequestLogEntry {
  Id: string;
  Time: string;
  ModelId: string;
  ConnectionId: string;
  ConnectionName: string;
  Caller: string | null;
  SubjectId: string | null;
  RelationshipId: string | null;
  /** 0 where nothing answered. */
  Status: number;
  DurationMilliseconds: number;
  BodyBytes: number;
  BodyKeptBytes: number;
}

/** The broker answers 410 for an entry made before the period its log keeps, and 404 for one it does not
 *  show this reader, so a page can say which. */
export type RequestLookup =
  | { kind: 'found'; entry: RequestLogEntry }
  | { kind: 'noLongerKept' }
  | { kind: 'notShown' };

export function connectionParameter(connection: string | undefined): string {
  return connection ? `connection=${encodeURIComponent(connection)}` : '';
}

/** The newest entries of the last day, oldest first. */
export function fetchLatestRequests(connection?: string): Promise<RequestLogEntry[]> {
  const narrowing = connectionParameter(connection);
  return apiClient.get<RequestLogEntry[]>(`/api/requests${narrowing ? `?${narrowing}` : ''}`);
}

export async function fetchRequest(id: string): Promise<RequestLookup> {
  try {
    return { kind: 'found', entry: await apiClient.get<RequestLogEntry>(`/api/requests/${encodeURIComponent(id)}`) };
  } catch (error) {
    if (error instanceof ApiError && error.status === 410) return { kind: 'noLongerKept' };
    if (error instanceof ApiError && error.status === 404) return { kind: 'notShown' };
    throw error;
  }
}

/** One hour's entries as the broker keeps them, a line apiece, or null where nothing was recorded in that
 *  hour. Fetched with the bearer token rather than a plain link so the token never lands in an address. */
export async function fetchRequestHour(hour: string): Promise<{ blob: Blob; fileName: string } | null> {
  const token = await apiClient.ensureToken();
  const response = await fetch(`${BASE_URL}/api/requests/download?hour=${encodeURIComponent(hour)}`, {
    headers: { Authorization: `Bearer ${token}` },
    credentials: 'include',
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new ApiError(response.status, await response.text());

  return {
    blob: await response.blob(),
    fileName: fileNameFromContentDisposition(response.headers.get('Content-Disposition'), `requests-${hour}.jsonl`),
  };
}

/** The hour as the broker names its files: `yyyyMMddHH`, in universal time. */
export function hourOf(instant: Date): string {
  return instant.toISOString().slice(0, 13).replace(/[-T]/g, '');
}

// Times are compared as instants: the broker writes a varying number of fractional digits, so the text does
// not sort. Each entry's is read once, since a busy broker streams many entries into a long list.
const instants = new WeakMap<RequestLogEntry, number>();

function instantOf(entry: RequestLogEntry): number {
  let instant = instants.get(entry);
  if (instant === undefined) {
    instant = Date.parse(entry.Time);
    instants.set(entry, instant);
  }
  return instant;
}

function newerFirst(a: RequestLogEntry, b: RequestLogEntry): number {
  return instantOf(b) - instantOf(a) || b.Id.localeCompare(a.Id);
}

/** `held`, already newest first, with each arrived entry it lacks put in its place, no longer than `limit`.
 *  `held` itself where nothing arrived is new, so a stream replaying what the page holds redraws nothing. */
export function mergeNewestFirst(held: RequestLogEntry[], arrived: RequestLogEntry[], limit: number): RequestLogEntry[] {
  const known = new Set(held.map((entry) => entry.Id));
  const fresh: RequestLogEntry[] = [];
  for (const entry of arrived) {
    if (known.has(entry.Id)) continue;
    known.add(entry.Id);
    fresh.push(entry);
  }
  if (fresh.length === 0) return held;

  const merged: RequestLogEntry[] = [];
  let heldIndex = 0;
  for (const entry of fresh.sort(newerFirst)) {
    while (heldIndex < held.length && newerFirst(held[heldIndex], entry) < 0) merged.push(held[heldIndex++]);
    merged.push(entry);
  }
  return merged.concat(held.slice(heldIndex)).slice(0, limit);
}
