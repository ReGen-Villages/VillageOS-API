import { apiClient } from './client';
import { unwrapProperties } from '../utils/propertyMapper';
import type { TemporalSnapshot, VosThing } from '../types/vos';

/** Counts returned by POST /api/model/fragment. */
export interface FragmentResult {
  thingsCreated: number;
  thingsUpdated: number;
  relationshipsCreated: number;
  things: VosThing[];
}

export const modelApi = {
  get: () => apiClient.getText('/api/model'),

  getAtTime: async (timestamp: string) => {
    const data = await apiClient.get<TemporalSnapshot>(
      `/api/model?timestamp=${encodeURIComponent(timestamp)}`,
    );
    return {
      ...data,
      Things: data.Things.map((t) => ({ ...t, Properties: unwrapProperties(t.Properties) })),
      Relationships: data.Relationships.map((r) => ({ ...r, Properties: unwrapProperties(r.Properties) })),
    } as TemporalSnapshot;
  },

  set: (modelJson: string) => {
    const parsed = JSON.parse(modelJson);
    return apiClient.post<unknown>('/api/model', parsed);
  },

  // Upsert a fragment ({ Name, Things, Relationships }) into the live model. Idempotent: re-applying
  // the same fragment neither duplicates nor errors; created Things/edges animate over SSE. Contrast
  // `set`, which replaces the whole model.
  applyFragment: (fragmentJson: string) => {
    const parsed = JSON.parse(fragmentJson);
    return apiClient.post<FragmentResult>('/api/model/fragment', parsed);
  },

  clear: () => apiClient.del<{ message: string }>('/api/model'),
};
