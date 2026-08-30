import { apiClient } from './client';
import type {
  ThingsInStateResponse,
  ObjectStatesResponse,
  StateTransitionsResponse,
  StateOccurrencesResponse,
} from '../types/vos';
import { thingsInStatePath, type StateNarrowing } from './stateQuery';

function withWindow(path: string, from?: string, to?: string): string {
  const query = new URLSearchParams();
  if (from) query.set('from', from);
  if (to) query.set('to', to);
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}

export const stateApi = {
  getThingsInState: (stateName: string, narrowing?: StateNarrowing) =>
    apiClient.get<ThingsInStateResponse>(thingsInStatePath(stateName, narrowing)),

  /** Current derived states of one Thing (its range evaluations that currently hold). */
  getThingStates: (thingId: string, signal?: AbortSignal) =>
    apiClient.get<ObjectStatesResponse>(`/api/things/${thingId}/states`, signal),

  /** When a Thing entered and exited each derived state, with the triggering property write.
   *  The response's `Coverage` states how far back the history reaches. */
  getStateTransitions: (thingId: string, from?: string, to?: string, signal?: AbortSignal) =>
    apiClient.get<StateTransitionsResponse>(withWindow(`/api/things/${thingId}/state-transitions`, from, to), signal),

  /** Intervals one Thing held one named state. */
  getStateOccurrences: (thingId: string, stateName: string, from?: string, to?: string, signal?: AbortSignal) =>
    apiClient.get<StateOccurrencesResponse>(
      withWindow(`/api/things/${thingId}/states/${encodeURIComponent(stateName)}/occurrences`, from, to),
      signal,
    ),
};
