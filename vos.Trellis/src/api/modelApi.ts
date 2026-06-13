import { apiClient } from './client';
import { unwrapProperties } from '../utils/propertyMapper';
import type { TemporalSnapshot } from '../types/vos';

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

  clear: () => apiClient.del<{ message: string }>('/api/model'),
};
