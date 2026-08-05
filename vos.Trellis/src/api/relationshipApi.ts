import { apiClient } from './client';
import type { VosRelationship, EffectiveProperty } from '../types/vos';
import { unwrapRelationship } from '../utils/propertyMapper';

async function writeProperty(id: string, name: string, type: string, value: unknown) {
  const rel = await apiClient.put<VosRelationship>(`/api/relationships/${id}/properties`, { Name: name, Type: type, Value: value });
  return unwrapRelationship(rel);
}

export const relationshipApi = {
  getAll: async () => {
    const rels = await apiClient.get<VosRelationship[]>('/api/relationships');
    return rels.map(unwrapRelationship);
  },

  get: async (id: string) => {
    const rel = await apiClient.get<VosRelationship>(`/api/relationships/${id}`);
    return unwrapRelationship(rel);
  },

  create: async (subjectId: string, predicateId: string, targetId: string) => {
    const rel = await apiClient.post<VosRelationship>('/api/relationships', {
      SubjectId: subjectId,
      PredicateId: predicateId,
      TargetId: targetId,
    });
    return unwrapRelationship(rel);
  },

  remove: (id: string) => apiClient.del<{ message: string }>(`/api/relationships/${id}`),

  setProperty: writeProperty,

  // A relationship has no separate create route: the one call creates the property when it is
  // absent and updates it when it is not. Named alongside thingApi.addProperty so a caller adding
  // a property makes the same call whichever of the two it is holding.
  addProperty: writeProperty,

  deleteProperty: (id: string, name: string) =>
    apiClient.del<{ message: string }>(`/api/relationships/${id}/properties/${encodeURIComponent(name)}`),

  // Own and inherited resolved together, each carrying its declared type and where it came from —
  // the same shape, served by the same handler, as the Thing route of the same name.
  getEffectiveProperties: (id: string) =>
    apiClient.get<Record<string, EffectiveProperty>>(`/api/relationships/${id}/properties`),
};
