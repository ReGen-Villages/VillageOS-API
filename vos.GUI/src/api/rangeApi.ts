import { apiClient } from './client';
import type { ThingRangeSummary, RelationshipRangesResponse, RelationshipStates } from '../types/vos';

export const rangeApi = {
  getSummary: (thingId: string) =>
    apiClient.get<ThingRangeSummary>(`/api/things/${thingId}/range-summary`),
};

export const relationshipRangeApi = {
  getAll: (relId: string) =>
    apiClient.get<RelationshipRangesResponse>(`/api/relationships/${relId}/ranges`),

  getStates: (relId: string) =>
    apiClient.get<RelationshipStates>(`/api/relationships/${relId}/states`),
};
