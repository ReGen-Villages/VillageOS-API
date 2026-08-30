/**
 * The signed-in application's answer to {@link ModelReads}: ask the broker.
 *
 * One of these belongs to one refresh generation. Everything resolved against it asks the broker once
 * per question, however many rows and widgets want the answer, and the sharing dies with the
 * generation — a map outliving it would serve a membership the model has since moved past.
 */
import { apiClient } from './client';
import type { ModelReads } from './modelReads';
import { stateApi } from './stateApi';
import { thingsInStatePath, type StateNarrowing } from './stateQuery';
import { rangeApi } from './rangeApi';
import { temporalApi } from './temporalApi';
import type { ThingRangesResponse, ThingsInStateResponse } from '../types/vos';

export function brokerModelReads(): ModelReads {
  const states = new Map<string, Promise<ThingsInStateResponse>>();
  const ranges = new Map<string, Promise<ThingRangesResponse | null>>();

  return {
    // The question is the request, not the state name: two widgets narrowing one state differently
    // must not be handed each other's answer, which would put a wrong number on screen with nothing
    // to say so.
    thingsInState(state: string, narrowing?: StateNarrowing) {
      const question = thingsInStatePath(state, narrowing);
      const inFlight = states.get(question);
      if (inFlight) return inFlight;
      const request = stateApi.getThingsInState(state, narrowing);
      states.set(question, request);
      // A failed read is dropped rather than shared: one broker hiccup would otherwise stick to
      // every later reader of this generation, with nothing to retry it until the next refresh.
      request.catch(() => states.delete(question));
      return request;
    },

    thingRanges(thingId: string) {
      const inFlight = ranges.get(thingId);
      if (inFlight) return inFlight;
      const request = rangeApi.getAll(thingId).catch(() => null);
      ranges.set(thingId, request);
      return request;
    },

    aggregate: (query) => temporalApi.aggregate(query),

    fromService: (endpoint, body) => apiClient.post<unknown>(endpoint, body),
  };
}
