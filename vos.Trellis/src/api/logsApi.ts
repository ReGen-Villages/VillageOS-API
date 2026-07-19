import { apiClient, ApiError } from './client';
import { fileNameFromContentDisposition, fullLogFallbackName } from '../utils/logDownload';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/**
 * The whole current log file from the broker — the Mycelium log, or a service daemon's log when
 * `service` is given. Unlike the tail/stream endpoints this is not line-capped, so it is the
 * source for "download the full log". Fetched with the bearer token rather than a plain link so
 * the token never lands in a URL or the browser history.
 */
export async function fetchFullLog(service?: string): Promise<{ blob: Blob; fileName: string }> {
  const token = await apiClient.ensureToken();
  const query = service ? `?service=${encodeURIComponent(service)}` : '';

  const resp = await fetch(`${BASE_URL}/api/logs/download${query}`, {
    headers: { Authorization: `Bearer ${token}` },
    credentials: 'include',
  });
  if (!resp.ok) throw new ApiError(resp.status, await resp.text());

  return {
    blob: await resp.blob(),
    fileName: fileNameFromContentDisposition(
      resp.headers.get('Content-Disposition'),
      fullLogFallbackName(service),
    ),
  };
}
