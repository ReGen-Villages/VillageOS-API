import { apiClient } from './client';
import type { PropertyVersionsResponse, ModelMutations, ThingMutations, RelationshipMutations } from '../types/vos';

function timeParams(start?: string, end?: string): string {
  const params = new URLSearchParams();
  if (start) params.set('startTime', start);
  if (end) params.set('endTime', end);
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export const temporalApi = {
  getPropertyVersions: (thingId: string, propertyName: string, start?: string, end?: string) =>
    apiClient.get<PropertyVersionsResponse>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/versions${timeParams(start, end)}`,
    ),

  getModelMutations: (start?: string, end?: string) =>
    apiClient.get<ModelMutations>(`/api/mutations${timeParams(start, end)}`),

  getThingMutations: (thingId: string, start?: string, end?: string) =>
    apiClient.get<ThingMutations>(`/api/things/${thingId}/mutations${timeParams(start, end)}`),

  getRelationshipMutations: (relId: string, start?: string, end?: string) =>
    apiClient.get<RelationshipMutations>(`/api/relationships/${relId}/mutations${timeParams(start, end)}`),
};
