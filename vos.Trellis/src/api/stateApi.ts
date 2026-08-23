import { apiClient } from './client';
import type {
  ThingsInStateResponse,
  ObjectStatesResponse,
  StateTransitionsResponse,
  StateOccurrencesResponse,
} from '../types/vos';

/** How a state answer is narrowed on the server: which Things come back, and which of their values
 *  ride along. Every field is one the endpoint accepts, so a caller narrows by asking a smaller
 *  question rather than by throwing most of a large answer away. */
export interface StateNarrowing {
  /** Further states a Thing must hold as well as the one being asked about. */
  alsoIn?: string[];
  /** States that disqualify a Thing — how a caller asks for the absence of one. */
  notIn?: string[];
  /** Only Things that `is` this type, followed through the whole chain. */
  type?: string;
  /** Only Things this container reaches through {@link withinPredicate}, at any depth. */
  within?: string;
  /** The predicate the containment is written with; the caller names it, so nothing here carries a
   *  vocabulary of its own. */
  withinPredicate?: string;
  /** The kinds Things `is` hold states too, and are left out unless this asks for them. */
  includeArchetypes?: boolean;
  /** The most Things to answer with, taken in name order. */
  limit?: number;
  /** Property names returned beside each id, so a row arrives complete. */
  properties?: string[];
}

/** The request one narrowed read makes. Exported because a caller sharing one in-flight request per
 *  question needs to know what the question is: two widgets narrowing the same state differently
 *  must not be handed each other's answer. */
export function thingsInStatePath(stateName: string, narrowing?: StateNarrowing): string {
  const path = `/api/states/${encodeURIComponent(stateName)}/things`;
  if (!narrowing) return path;
  const query = new URLSearchParams();
  if (narrowing.alsoIn?.length) query.set('alsoIn', narrowing.alsoIn.join(','));
  if (narrowing.notIn?.length) query.set('notIn', narrowing.notIn.join(','));
  if (narrowing.type) query.set('type', narrowing.type);
  // The endpoint refuses a container without the predicate it is reached by, so half a scope is not
  // a question worth spending a request on.
  if (narrowing.within && narrowing.withinPredicate) {
    query.set('within', narrowing.within);
    query.set('withinPredicate', narrowing.withinPredicate);
  }
  if (narrowing.includeArchetypes) query.set('includeArchetypes', 'true');
  if (narrowing.limit !== undefined) query.set('limit', String(narrowing.limit));
  if (narrowing.properties?.length) query.set('properties', narrowing.properties.join(','));
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}

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
