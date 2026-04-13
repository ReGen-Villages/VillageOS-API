import { apiClient } from './client';
import type {
  ThingRangeSummary,
  RelationshipRangesResponse,
  RelationshipStates,
  RangeDto,
  CreateRangeRequest,
  CriteriaValidationResult,
} from '../types/vos';

export const rangeApi = {
  getSummary: (thingId: string) =>
    apiClient.get<ThingRangeSummary>(`/api/things/${thingId}/range-summary`),

  getAll: (thingId: string) =>
    apiClient.get<RangeDto[]>(`/api/things/${thingId}/ranges`),

  get: (thingId: string, rangeName: string) =>
    apiClient.get<RangeDto>(`/api/things/${thingId}/ranges/${encodeURIComponent(rangeName)}`),

  create: (thingId: string, body: CreateRangeRequest) =>
    apiClient.post<RangeDto>(`/api/things/${thingId}/ranges`, body),

  delete: (thingId: string, rangeName: string) =>
    apiClient.del<void>(`/api/things/${thingId}/ranges/${encodeURIComponent(rangeName)}`),

  validateCriteria: (criteria: string) =>
    apiClient.post<CriteriaValidationResult>('/api/ranges/validate', { Criteria: criteria }),
};

export const relationshipRangeApi = {
  getAll: (relId: string) =>
    apiClient.get<RelationshipRangesResponse>(`/api/relationships/${relId}/ranges`),

  getStates: (relId: string) =>
    apiClient.get<RelationshipStates>(`/api/relationships/${relId}/states`),
};
