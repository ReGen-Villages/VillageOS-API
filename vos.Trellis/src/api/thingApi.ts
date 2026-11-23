import { apiClient, ApiError } from './client';
import type { VosThing, EffectiveProperty } from '@tendril-ui/tendril';
import type { VosTypeName } from '../utils/constants';
import { unwrapThing } from '@tendril-ui/tendril';

export const thingApi = {
  // `properties` narrows each Thing to the names the model says it is drawn with. Undefined asks for
  // everything, which a model that declares nothing wants, because its properties may be live values a
  // dashboard is showing.
  getAll: async (properties?: readonly string[]) => {
    const query = properties?.length ? `?properties=${encodeURIComponent(properties.join(','))}` : '';
    const things = await apiClient.get<VosThing[]>(`/api/things${query}`);
    return things.map(unwrapThing);
  },

  /** A 404 means the model binds nothing under the role, which is an answer rather than a failure. */
  getBoundBy: async (roleId: string) => {
    try {
      const thing = await apiClient.get<VosThing>(`/api/things?boundBy=${roleId}`);
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

  /** The values it held then, with nothing worked out from the model now. Null where the Thing did not yet
   *  exist or that past has been reclaimed. A Thing retracted since still answers, because this route
   *  resolves before the filter that hides retracted Things. */
  getAtInstant: async (id: string, instant: string, signal?: AbortSignal) => {
    try {
      const thing = await apiClient.get<VosThing>(
        `/api/things/${id}?timestamp=${encodeURIComponent(instant)}`,
        signal,
      );
      return unwrapThing(thing);
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) return null;
      throw error;
    }
  },

  // Whether the Thing is a type is fixed when it is created, and the platform refuses a Thing typed by
  // one that was not declared a type, so the caller always says which it is making.
  create: async (name: string, isArchetype: boolean) => {
    const thing = await apiClient.action(`create Thing "${name}"`, () =>
      apiClient.post<VosThing>('/api/things', { Name: name, IsArchetype: isArchetype }),
    );
    return unwrapThing(thing);
  },

  remove: (id: string) =>
    apiClient.action(`delete Thing ${id}`, () => apiClient.del<{ message: string }>(`/api/things/${id}`)),

  // Keeps the Thing's identifier and relationships, unlike deleting and creating it again; the broker records
  // the rename, so it reaches every page and a past read still finds the old name.
  rename: async (id: string, name: string) => {
    const thing = await apiClient.action(`rename Thing ${id} to "${name}"`, () =>
      apiClient.put<VosThing>(`/api/things/${id}/name`, { Name: name }),
    );
    return unwrapThing(thing);
  },

  // Updates only: the platform refuses a property that does not exist, and keeps the type it already has
  // whatever `type` says. addProperty creates one, and its `type` decides what the property holds.
  setProperty: async (id: string, name: string, type: VosTypeName, value: unknown) => {
    const thing = await apiClient.action(`set property "${name}" on Thing ${id}`, () =>
      apiClient.put<VosThing>(`/api/things/${id}/properties`, { Name: name, Type: type, Value: value }),
    );
    return unwrapThing(thing);
  },

  addProperty: async (id: string, name: string, type: VosTypeName, value: unknown) => {
    const thing = await apiClient.action(`add property "${name}" to Thing ${id}`, () =>
      apiClient.post<VosThing>(`/api/things/${id}/properties`, { Name: name, Type: type, Value: value }),
    );
    return unwrapThing(thing);
  },

  deleteProperty: (id: string, name: string) =>
    apiClient.action(`delete property "${name}" from Thing ${id}`, () =>
      apiClient.del<{ message: string }>(`/api/things/${id}/properties/${encodeURIComponent(name)}`),
    ),

  getEffectiveProperties: (id: string) =>
    apiClient.get<Record<string, EffectiveProperty>>(`/api/things/${id}/properties`),

  // A Thing's own value replaces the one it inherits. Without `ids` this reads every Thing, more than the
  // model load itself on a large model, so only a page working across the whole model should leave it out.
  getAllProperties: (ids?: readonly string[]) => {
    const narrowing = ids?.length ? `&ids=${encodeURIComponent(ids.join(','))}` : '';
    return apiClient.get<Record<string, Record<string, EffectiveProperty>>>(
      `/api/things/properties?scope=effective${narrowing}`,
    );
  },
};
