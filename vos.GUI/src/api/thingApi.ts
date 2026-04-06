import { apiClient } from './client';
import type { VosThing, EffectiveProperty } from '../types/vos';
import { unwrapThing } from '../utils/propertyMapper';

export const thingApi = {
  getAll: async () => {
    const things = await apiClient.get<VosThing[]>('/api/things');
    return things.map(unwrapThing);
  },

  // ── Phase loading (large models) ──────────────────────────────────────
  /** Phase 1: Surface things without geometry — fast initial render using lat/lng. */
  getSurfaceThings: async () => {
    const things = await apiClient.get<VosThing[]>('/api/things/phase/surface');
    return things.map(unwrapThing);
  },

  /** Phase 2: Remaining (non-surface) things. */
  getRemainingThings: async () => {
    const things = await apiClient.get<VosThing[]>('/api/things/phase/remaining');
    return things.map(unwrapThing);
  },

  get: async (id: string) => {
    const thing = await apiClient.get<VosThing>(`/api/things/${id}`);
    return unwrapThing(thing);
  },

  create: async (name: string) => {
    const thing = await apiClient.post<VosThing>('/api/things', { Name: name });
    return unwrapThing(thing);
  },

  remove: (id: string) => apiClient.del<{ message: string }>(`/api/things/${id}`),

  setProperty: async (id: string, name: string, type: string, value: unknown) => {
    const thing = await apiClient.put<VosThing>(`/api/things/${id}/properties`, { Name: name, Type: type, Value: value });
    return unwrapThing(thing);
  },

  deleteProperty: (id: string, name: string) =>
    apiClient.del<{ message: string }>(`/api/things/${id}/properties/${encodeURIComponent(name)}`),

  getEffectiveProperties: (id: string) =>
    apiClient.get<Record<string, EffectiveProperty>>(`/api/things/${id}/effective-properties`),
};
