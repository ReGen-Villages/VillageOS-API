import { apiClient } from './client';
import { unwrapProperties } from '@tendril-ui/tendril';
import type { TemporalSnapshot, VosThing } from '@tendril-ui/tendril';

/** Counts returned by POST /api/model/fragment. */
export interface FragmentResult {
  thingsCreated: number;
  thingsUpdated: number;
  relationshipsCreated: number;
  things: VosThing[];
}

export const modelApi = {
  get: () => apiClient.getText('/api/model'),

  getAtTime: async (timestamp: string, signal?: AbortSignal) => {
    const data = await apiClient.get<TemporalSnapshot>(
      `/api/model?timestamp=${encodeURIComponent(timestamp)}`,
      signal,
    );
    return {
      ...data,
      Things: data.Things.map((t) => ({ ...t, Properties: unwrapProperties(t.Properties) })),
      Relationships: data.Relationships.map((r) => ({ ...r, Properties: unwrapProperties(r.Properties) })),
    } as TemporalSnapshot;
  },

  set: (modelJson: string) => {
    const parsed = JSON.parse(modelJson);
    return apiClient.action('replace the whole model', () => apiClient.post<unknown>('/api/model', parsed));
  },

  // Upsert a fragment ({ Name, Things, Relationships }) into the live model. Idempotent: re-applying
  // the same fragment neither duplicates nor errors; created Things/relationships animate over SSE. Contrast
  // `set`, which replaces the whole model.
  applyFragment: (fragmentJson: string) => {
    const parsed = JSON.parse(fragmentJson);
    return apiClient.action(
      `apply fragment "${parsed.Name ?? 'unnamed'}"`,
      () => apiClient.post<FragmentResult>('/api/model/fragment', parsed),
    );
  },

  clear: () => apiClient.action('clear the model', () => apiClient.del<{ message: string }>('/api/model')),
};
