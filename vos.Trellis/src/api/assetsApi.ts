import { apiClient } from './client';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

interface KeptContent {
  bytes: ArrayBuffer;
  contentType: string;
}

export const assetsApi = {
  /** The token goes in a header, because a plain <img src> carries no credential and a token must never
   *  land in an address, a log or the history. Null on any refusal, so a reference that cannot be
   *  resolved shows as text rather than breaking the panel. */
  async getContent(ticket: string): Promise<KeptContent | null> {
    const token = await apiClient.ensureToken();
    const response = await fetch(`${BASE_URL}/api/assets/${ticket}`, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'include',
    });
    if (!response.ok) return null;
    return {
      bytes: await response.arrayBuffer(),
      contentType: response.headers.get('Content-Type') || 'application/octet-stream',
    };
  },
};
