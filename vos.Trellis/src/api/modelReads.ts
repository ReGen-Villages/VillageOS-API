/**
 * The four reads a binding makes that a loaded model cannot answer for itself.
 *
 * Everything else `resolveBinding` needs is in the model index it was handed. These four are not:
 * which Things hold a derived state, the judge-ranges over one Thing, a reduction over history, and
 * whatever a model-side service answers. A resolver that reached for them itself could only ever run
 * where the broker is reachable — which is every signed-in page, and none of the page a submitter
 * opens holding no credential. So the resolver asks whoever built its context, and the two callers
 * answer differently: the signed-in application from the broker, the submitter's page from what the
 * intake service handed it.
 */
import type {
  TemporalAggregateQuery,
  TemporalAggregateResponse,
  ThingRangesResponse,
  ThingsInStateResponse,
} from '../types/vos';
import type { StateNarrowing } from './stateQuery';

export interface ModelReads {
  /** Which Things hold a state, narrowed as the question allows. */
  thingsInState(state: string, narrowing?: StateNarrowing): Promise<ThingsInStateResponse>;
  /** One Thing's ranges, own and inherited. Null where the read failed: a verdict the model holds
   *  still reads, without the target it names. */
  thingRanges(thingId: string): Promise<ThingRangesResponse | null>;
  /** A reduction over a trailing window. */
  aggregate(query: TemporalAggregateQuery): Promise<TemporalAggregateResponse>;
  /** Whatever a model-side service answers when a spec names one. */
  fromService(endpoint: string, body: unknown): Promise<unknown>;
}
