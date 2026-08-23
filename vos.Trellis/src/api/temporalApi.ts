import { apiClient } from './client';
import type {
  PropertyVersionsResponse,
  ModelMutations,
  ThingMutations,
  RelationshipMutations,
  PropertyFactsResponse,
  TemporalAggregateQuery,
  TemporalAggregateResponse,
} from '../types/vos';

function timeParams(start?: string, end?: string): string {
  const params = new URLSearchParams();
  if (start) params.set('startTime', start);
  if (end) params.set('endTime', end);
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export const temporalApi = {
  /** Reduce a type's instances into time buckets across the trailing window — the shape roll-ups
   *  (which reduce over membership) and property history (which returns one property's past values)
   *  cannot express between them. A question the platform cannot answer is refused, so a caller
   *  hears about it rather than drawing an empty series. */
  aggregate: (query: TemporalAggregateQuery) =>
    apiClient.post<TemporalAggregateResponse>('/api/temporal/aggregate', query),

  getPropertyVersions: (thingId: string, propertyName: string, start?: string, end?: string) =>
    apiClient.get<PropertyVersionsResponse>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/versions${timeParams(start, end)}`,
    ),

  getModelMutations: (start?: string, end?: string) =>
    apiClient.get<ModelMutations>(`/api/mutations${timeParams(start, end)}`),

  getThingMutations: (thingId: string, start?: string, end?: string, signal?: AbortSignal) =>
    apiClient.get<ThingMutations>(`/api/things/${thingId}/mutations${timeParams(start, end)}`, signal),

  getRelationshipMutations: (relId: string, start?: string, end?: string, signal?: AbortSignal) =>
    apiClient.get<RelationshipMutations>(
      `/api/relationships/${relId}/mutations${timeParams(start, end)}`,
      signal,
    ),

  /** Commit-Log Fact history for one property — carries the author (the service that
   *  wrote the value) and commit sequence, so it attributes each change to its sender. */
  getPropertyFacts: (thingId: string, propertyName: string, signal?: AbortSignal) =>
    apiClient.get<PropertyFactsResponse>(
      `/api/things/${thingId}/properties/${encodeURIComponent(propertyName)}/facts`,
      signal,
    ),
};
