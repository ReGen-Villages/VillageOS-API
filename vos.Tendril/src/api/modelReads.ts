/**
 * The reads a binding makes that a loaded model cannot answer for itself.
 *
 * Everything else `resolveBinding` needs is in the model index it was handed. These are not: which
 * Things hold a derived state, the ranges judging one Thing, a reduction over Things by time bucket,
 * a reduction over one property's history, and whatever a model-side service answers. A resolver
 * that reached for them itself could only ever run
 * where the broker is reachable — which is every signed-in page, and none of the page a submitter
 * opens holding no credential. So the resolver asks whoever built its context, and the two callers
 * answer differently: the signed-in application from the broker, the submitter's page from what the
 * intake service handed it.
 */
import type {
  TemporalAggregateQuery,
  TemporalAggregateResponse,
  TemporalReduceQuery,
  TemporalReduceResponse,
  ThingRangesResponse,
  ThingsInStateResponse,
} from '../types/vos';
import type { StateNarrowing } from './stateQuery';

export interface ModelReads {
  thingsInState(state: string, narrowing?: StateNarrowing): Promise<ThingsInStateResponse>;
  /** One Thing's ranges, own and inherited. Null where the read failed: a verdict the model holds
   *  still reads, without the target it names. */
  thingRanges(thingId: string): Promise<ThingRangesResponse | null>;
  aggregate(query: TemporalAggregateQuery): Promise<TemporalAggregateResponse>;
  reduce(query: TemporalReduceQuery): Promise<TemporalReduceResponse>;
  fromService(endpoint: string, body: unknown): Promise<unknown>;
  /** Where a writing widget posts a press, apart from where a `service` binding reads, because a press
   *  carries who is pressing and a read does not. Absent where the page cannot press. */
  press?(endpoint: string, body: Record<string, unknown>): Promise<unknown>;
  /** Tells the broker a person asked for a change through a widget. Absent where the page holds no
   *  credential and so has nobody to tell. */
  recordAction?(description: string, succeeded: boolean): void;
  /** This and `stampTime` are absent where the page holds no credential and so cannot write. */
  relate?(subjectId: string, predicateId: string, targetId: string): Promise<void>;
  stampTime?(thingId: string, property: string, at: Date): Promise<void>;
}
