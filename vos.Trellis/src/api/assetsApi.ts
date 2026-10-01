import { apiClient } from './client';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

export interface KeptContent {
  bytes: ArrayBuffer;
  contentType: string;
}

export const assetsApi = {
  /**
   * The content a ticket names, from the broker's asset store. Fetched with the bearer token in
   * a header rather than linked as a plain address, because an address carries no credential —
   * a bare <img src> would be refused — and a token must never land in a URL or the history.
   * Null when the store holds nothing for the ticket, and on any other refusal too: a reference
   * that cannot be resolved degrades to text, it does not take the panel down.
   */
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
