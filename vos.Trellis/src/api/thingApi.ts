import { apiClient, ApiError } from './client';
import type { VosThing, EffectiveProperty } from '../types/vos';
import type { VosTypeName } from '../utils/constants';
import { unwrapThing } from '../utils/propertyMapper';

export const thingApi = {
  // `properties` narrows what each Thing carries to the names the model declared it is drawn with
  // (#6188). Undefined asks for everything, which is what a model that declares nothing wants — its
  // properties may be the live values a dashboard is watching, and deferring those helps nobody.
  getAll: async (properties?: readonly string[]) => {
    const query = properties?.length ? `?properties=${encodeURIComponent(properties.join(','))}` : '';
    const things = await apiClient.get<VosThing[]>(`/api/things${query}`);
    return things.map(unwrapThing);
  },

  /** The single Thing with this name, or null when the model has none — a model with nothing to say
   *  is answered with a 404, which is an answer rather than a failure. */
  getByName: async (name: string) => {
    try {
      const thing = await apiClient.get<VosThing>(`/api/things?name=${encodeURIComponent(name)}`);
      return unwrapThing(thing);
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) return null;
      throw error;
    }
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

  // Rename a Thing in place (#5862) — keeps its Id and all edges (unlike delete+recreate). The broker
  // persists a NameSet Fact, so the change streams over SSE and is temporally reconstructable.
  rename: async (id: string, name: string) => {
    const thing = await apiClient.put<VosThing>(`/api/things/${id}/name`, { Name: name });
    return unwrapThing(thing);
  },

  // Update only: the platform answers "Property does not exist on the thing" when the property is
  // absent, and keeps the type the property already has whatever `type` says. Use addProperty to
  // create one — that is the call whose `type` decides what the property will hold.
  setProperty: async (id: string, name: string, type: VosTypeName, value: unknown) => {
    const thing = await apiClient.put<VosThing>(`/api/things/${id}/properties`, { Name: name, Type: type, Value: value });
    return unwrapThing(thing);
  },

  addProperty: async (id: string, name: string, type: VosTypeName, value: unknown) => {
    const thing = await apiClient.post<VosThing>(`/api/things/${id}/properties`, { Name: name, Type: type, Value: value });
    return unwrapThing(thing);
  },

  deleteProperty: (id: string, name: string) =>
    apiClient.del<{ message: string }>(`/api/things/${id}/properties/${encodeURIComponent(name)}`),

  getEffectiveProperties: (id: string) =>
    apiClient.get<Record<string, EffectiveProperty>>(`/api/things/${id}/properties`),

  // Every thing's resolved properties in one call, keyed by thing id. scope: effective (default,
  // own + inherited with own/overrides winning) | own | inherited. Each property carries its own
  // provenance (IsInherited / InheritedFrom) regardless of scope. Used by bulk read-only surfaces
  // (Property Search) that need the full inherited view without a request per thing.
  getAllProperties: (scope: 'effective' | 'own' | 'inherited' = 'effective') =>
    apiClient.get<Record<string, Record<string, EffectiveProperty>>>(
      `/api/things/properties?scope=${scope}`,
    ),
};
