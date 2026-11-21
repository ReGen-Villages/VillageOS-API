import { apiClient } from './client';
import type {
  PropertyVersionsResponse,
  ModelMutations,
  ThingMutations,
  RelationshipMutations,
  TemporalAggregateQuery,
  TemporalAggregateResponse,
  TemporalReduceQuery,
  TemporalReduceResponse,
} from '@tendril-ui/tendril';

function timeParameters(start?: string, end?: string): string {
  const parameters = new URLSearchParams();
  if (start) parameters.set('startTime', start);
  if (end) parameters.set('endTime', end);
  const qs = parameters.toString();
  return qs ? `?${qs}` : '';
}

export const temporalApi = {
  /** Time buckets over a type's instances, which neither derived properties nor one property's history can
   *  express. A question the platform cannot answer is refused, so a caller hears of it rather than drawing
   *  an empty series. */
  aggregate: (query: TemporalAggregateQuery) =>
    apiClient.post<TemporalAggregateResponse>('/api/temporal/aggregate', query),

  reduce: (query: TemporalReduceQuery) =>
    apiClient.post<TemporalReduceResponse>('/api/temporal/reduce', query),

  getPropertyVersions: (thingId: string, propertyName: string, start?: string, end?: string) =>
    apiClient.get<PropertyVersionsResponse>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/versions${timeParameters(start, end)}`,
    ),

  getModelMutations: (start?: string, end?: string) =>
    apiClient.get<ModelMutations>(`/api/mutations${timeParameters(start, end)}`),

  getThingMutations: (thingId: string, start?: string, end?: string, signal?: AbortSignal) =>
    apiClient.get<ThingMutations>(`/api/things/${thingId}/mutations${timeParameters(start, end)}`, signal),

  getRelationshipMutations: (relationshipId: string, start?: string, end?: string, signal?: AbortSignal) =>
    apiClient.get<RelationshipMutations>(
      `/api/relationships/${relationshipId}/mutations${timeParameters(start, end)}`,
      signal,
    ),
};
