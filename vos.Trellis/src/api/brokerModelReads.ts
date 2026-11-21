/** One of these serves one refresh, so each question goes to the broker once however many widgets ask it,
 *  and nothing is kept past the refresh, where it would answer with a membership the model has since changed. */
import { apiClient } from './client';
import type { ModelReads } from '@tendril-ui/tendril';
import { stateApi } from './stateApi';
import { thingsInStatePath, type StateNarrowing } from '@tendril-ui/tendril';
import { rangeApi } from './rangeApi';
import { temporalApi } from './temporalApi';
import { relationshipApi } from './relationshipApi';
import { thingApi } from './thingApi';
import type { TemporalReduceResponse, ThingRangesResponse, ThingsInStateResponse } from '@tendril-ui/tendril';

export function brokerModelReads(): ModelReads {
  const states = new Map<string, Promise<ThingsInStateResponse>>();
  const ranges = new Map<string, Promise<ThingRangesResponse | null>>();
  const reductions = new Map<string, Promise<TemporalReduceResponse>>();
  const services = new Map<string, Promise<unknown>>();

  return {
    // Keyed by the request, not the state name: two widgets narrowing one state differently must not get
    // each other's answer, which would show a wrong number with no warning.
    thingsInState(state: string, narrowing?: StateNarrowing) {
      const question = thingsInStatePath(state, narrowing);
      const inFlight = states.get(question);
      if (inFlight) return inFlight;
      const request = stateApi.getThingsInState(state, narrowing);
      states.set(question, request);
      // Dropped when it fails, or one failed read would answer every later reader until the next refresh.
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

    // A chart's series and the range beside it ask some of the same questions, so a refresh reads each once.
    reduce(query) {
      const question = JSON.stringify(query);
      const inFlight = reductions.get(question);
      if (inFlight) return inFlight;
      const request = temporalApi.reduce(query);
      reductions.set(question, request);
      request.catch(() => reductions.delete(question));
      return request;
    },

    // Shared only while in flight, because the widgets of one refresh ask together.
    fromService(endpoint, body) {
      const question = `${endpoint} ${JSON.stringify(body)}`;
      const inFlight = services.get(question);
      if (inFlight) return inFlight;
      const request = apiClient.post<unknown>(endpoint, body);
      services.set(question, request);
      request.then(() => services.delete(question), () => services.delete(question));
      return request;
    },

    press: (endpoint, body) => apiClient.post<unknown>(endpoint, body),

    recordAction(description, succeeded) {
      void apiClient.reportAction(description, succeeded);
    },

    async relate(subjectId, predicateId, targetId) {
      await relationshipApi.create(subjectId, predicateId, targetId);
    },

    async stampTime(thingId, property, at) {
      await thingApi.setProperty(thingId, property, 'vos.DateTime', at.toISOString());
    },
  };
}
