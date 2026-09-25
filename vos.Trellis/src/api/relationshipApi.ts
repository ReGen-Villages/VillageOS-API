import { apiClient } from './client';
import type { VosRelationship, EffectiveProperty } from '../types/vos';
import type { VosTypeName } from '../utils/constants';
import { unwrapRelationship } from '../utils/propertyMapper';

async function writeProperty(id: string, name: string, type: VosTypeName, value: unknown) {
  const relationship = await apiClient.action(`set property "${name}" on relationship ${id}`, () =>
    apiClient.put<VosRelationship>(`/api/relationships/${id}/properties`, { Name: name, Type: type, Value: value }),
  );
  return unwrapRelationship(relationship);
}

export const relationshipApi = {
  getAll: async () => {
    const relationships = await apiClient.get<VosRelationship[]>('/api/relationships');
    return relationships.map(unwrapRelationship);
  },

  get: async (id: string, signal?: AbortSignal) => {
    const relationship = await apiClient.get<VosRelationship>(`/api/relationships/${id}`, signal);
    return unwrapRelationship(relationship);
  },

  create: async (subjectId: string, predicateId: string, targetId: string) => {
    const relationship = await apiClient.action(`relate Thing ${subjectId} to Thing ${targetId}`, () =>
      apiClient.post<VosRelationship>('/api/relationships', {
        SubjectId: subjectId,
        PredicateId: predicateId,
        TargetId: targetId,
      }),
    );
    return unwrapRelationship(relationship);
  },

  remove: (id: string) =>
    apiClient.action(`delete relationship ${id}`, () => apiClient.del<{ message: string }>(`/api/relationships/${id}`)),

  setProperty: writeProperty,

  // A relationship has no separate create route: the one call creates the property when it is
  // absent and updates it when it is not. Named alongside thingApi.addProperty so a caller adding
  // a property makes the same call whichever of the two it is holding.
  addProperty: writeProperty,

  deleteProperty: (id: string, name: string) =>
    apiClient.action(`delete property "${name}" from relationship ${id}`, () =>
      apiClient.del<{ message: string }>(`/api/relationships/${id}/properties/${encodeURIComponent(name)}`),
    ),

  // Own and inherited resolved together, each carrying its declared type and where it came from —
  // the same shape, served by the same handler, as the Thing route of the same name.
  getEffectiveProperties: (id: string) =>
    apiClient.get<Record<string, EffectiveProperty>>(`/api/relationships/${id}/properties`),
};
