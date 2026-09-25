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
import type { TemporalReduceResponse, ThingRangesResponse, ThingsInStateResponse } from '../types/vos';

export function brokerModelReads(): ModelReads {
  const states = new Map<string, Promise<ThingsInStateResponse>>();
  const ranges = new Map<string, Promise<ThingRangesResponse | null>>();
  const reductions = new Map<string, Promise<TemporalReduceResponse>>();
  const services = new Map<string, Promise<unknown>>();

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

    // A chart of seven series asks seven questions, and the range beside it asks some of the same;
    // shared by the whole question, as a state read is, so one refresh walks a series once per
    // distinct question.
    reduce(query) {
      const question = JSON.stringify(query);
      const inFlight = reductions.get(question);
      if (inFlight) return inFlight;
      const request = temporalApi.reduce(query);
      reductions.set(question, request);
      request.catch(() => reductions.delete(question));
      return request;
    },

    // Shared only while in flight, unlike a state read: a widget that writes travels through this
    // same port, and a press answered from an earlier press's reply would record nothing and say
    // it had. Widgets of one refresh ask together, so their one question is still asked once.
    fromService(endpoint, body) {
      const question = `${endpoint} ${JSON.stringify(body)}`;
      const inFlight = services.get(question);
      if (inFlight) return inFlight;
      const request = apiClient.post<unknown>(endpoint, body);
      services.set(question, request);
      request.then(() => services.delete(question), () => services.delete(question));
      return request;
    },

    recordAction(description, succeeded) {
      void apiClient.reportAction(description, succeeded);
    },
  };
}
