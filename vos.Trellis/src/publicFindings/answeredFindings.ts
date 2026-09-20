/**
 * What the intake service answered, turned into what the resolver reads.
 *
 * The page holds no credential, so the questions a loaded model cannot answer are answered from the
 * same document rather than from the broker: which Things hold a state, off the states each Thing
 * arrived carrying, and a Thing's ranges, off the ranges the service read for it. A reduction over a
 * property's history is the one question asked after the document arrived — the intake service
 * proxies it under the page's ticket — so whoever builds the findings hands in the way to ask it. The
 * two a submitter's page never asks — a reduction over Things by time bucket, and a model-side
 * service — refuse rather than resolving to nothing, because nothing is how a figure the analysis has
 * not computed reads, and a question this page cannot ask is not that.
 */
import { buildModelIndex, type ModelIndex } from '../api/dashboardApi';
import type { ModelReads } from '../api/modelReads';
import type { DashboardSpecification } from '../types/dashboard';
import type {
  TemporalReduceQuery,
  TemporalReduceResponse,
  ThingRangesResponse,
  ThingsInStateResponse,
  VosRelationship,
  VosThing,
} from '../types/vos';
import { unwrapRelationship, unwrapThing } from '../utils/propertyMapper';

/** The wire shape, as the broker wrote it and the service passed it on. `States` is a Thing's derived
 *  states, which the application reads from the broker instead and so has no place for. */
type AnsweredThing = VosThing & { States?: string[] };

export interface FindingsAnswer {
  /** The wire's own name for the page, as the service serialises it. */
  spec: string;
  scopeId: string;
  things: AnsweredThing[];
  relationships: VosRelationship[];
  ranges: Record<string, ThingRangesResponse>;
}

export interface Findings {
  /** The page the model declares, parsed. Not localized: which language it reads in changes while the
   *  page is open, and what it draws does not. */
  specification: DashboardSpecification;
  index: ModelIndex;
  scopeId: string;
  reads: ModelReads;
}

/** Throws where the service answered a spec that is not one. Read here rather than while rendering, so
 *  a page that cannot be drawn says so where every other refusal is said, instead of drawing nothing —
 *  which is how a figure the analysis has not computed reads. */
export function findingsFrom(
  answer: FindingsAnswer,
  reduce: (query: TemporalReduceQuery) => Promise<TemporalReduceResponse> = () =>
    Promise.reject(new Error('This page was handed no way to reduce a property series.')),
): Findings {
  const specification = JSON.parse(answer.spec) as DashboardSpecification;
  const relationships = answer.relationships.map(unwrapRelationship);
  const holders = statesByThing(answer.things);

  return {
    specification,
    index: buildModelIndex(answer.things.map(unwrapThing), relationships),
    scopeId: answer.scopeId,
    reads: {
      thingsInState: (state, narrowing) => Promise.resolve(inState(state, answer.things, holders, narrowing?.countOnly)),
      thingRanges: (thingId) => Promise.resolve(answer.ranges[thingId] ?? null),
      aggregate: () => Promise.reject(new Error('A findings page reduces no Things by time bucket.')),
      reduce,
      fromService: () => Promise.reject(new Error('A findings page calls no service.')),
    },
  };
}

function statesByThing(things: AnsweredThing[]): Map<string, string[]> {
  return new Map(things.map((thing) => [thing.Id, thing.States ?? []]));
}

/** Answers the way the platform's state read does: the number alone when the count alone was asked
 *  for, the members otherwise. */
function inState(
  state: string,
  things: AnsweredThing[],
  holders: Map<string, string[]>,
  countOnly: boolean | undefined,
): ThingsInStateResponse {
  const holding = things.filter((thing) => holders.get(thing.Id)?.includes(state));
  return countOnly
    ? { StateName: state, Count: holding.length }
    : { StateName: state, Things: holding.map((thing) => ({ Id: thing.Id, Name: thing.Name })) };
}
