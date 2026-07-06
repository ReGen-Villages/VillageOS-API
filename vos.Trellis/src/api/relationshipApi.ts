import { apiClient } from './client';
import type { VosRelationship } from '../types/vos';
import { unwrapRelationship } from '../utils/propertyMapper';

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

  setProperty: async (id: string, name: string, type: string, value: unknown) => {
    const rel = await apiClient.put<VosRelationship>(`/api/relationships/${id}/properties`, { Name: name, Type: type, Value: value });
    return unwrapRelationship(rel);
  },

  deleteProperty: (id: string, name: string) =>
    apiClient.del<{ message: string }>(`/api/relationships/${id}/properties/${encodeURIComponent(name)}`),
};
