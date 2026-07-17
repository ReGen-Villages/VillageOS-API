import { apiClient } from './client';
import type { ThingsInStateResponse, ObjectStatesResponse } from '../types/vos';

export const stateApi = {
  getThingsInState: (stateName: string) =>
    apiClient.get<ThingsInStateResponse>(`/api/states/${encodeURIComponent(stateName)}/things`),

  /** Current derived states of one Thing (its range evaluations that currently hold). */
  getThingStates: (thingId: string, signal?: AbortSignal) =>
    apiClient.get<ObjectStatesResponse>(`/api/things/${thingId}/states`, signal),
};
