import { apiClient, ApiError } from './client';
import { fileNameFromContentDisposition, fullLogFallbackName } from '../utils/logDownload';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/** Not cut to a number of lines, unlike the tail and stream routes, so this is the full download. The token
 *  goes in a header so it never lands in an address or the browser's history. */
export async function fetchFullLog(service?: string): Promise<{ blob: Blob; fileName: string }> {
  const token = await apiClient.ensureToken();
  const query = service ? `?service=${encodeURIComponent(service)}` : '';

  const response = await fetch(`${BASE_URL}/api/logs/download${query}`, {
    headers: { Authorization: `Bearer ${token}` },
    credentials: 'include',
  });
  if (!response.ok) throw new ApiError(response.status, await response.text());

  return {
    blob: await response.blob(),
    fileName: fileNameFromContentDisposition(
      response.headers.get('Content-Disposition'),
      fullLogFallbackName(service),
    ),
  };
}
