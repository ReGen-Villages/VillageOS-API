/**
 * What the intake service answered, turned into what the resolver reads.
 *
 * The page holds no credential, so the four questions a loaded model cannot answer are answered from
 * the same document rather than from the broker: which Things hold a state, off the states each Thing
 * arrived carrying, and a Thing's ranges, off the ranges the service read for it. The two a submitter's
 * page never asks — a reduction over history, and a model-side service — refuse rather than resolving
 * to nothing, because nothing is how a figure the analysis has not computed reads, and a question this
 * page cannot ask is not that.
 */
import { buildModelIndex, type ModelIndex } from '../api/dashboardApi';
import type { ModelReads } from '../api/modelReads';
import type { DashboardSpec } from '../types/dashboard';
import type { ThingRangesResponse, ThingsInStateResponse, VosRelationship, VosThing } from '../types/vos';
import { unwrapRelationship, unwrapThing } from '../utils/propertyMapper';

/** The wire shape, as the broker wrote it and the service passed it on. `States` is a Thing's derived
 *  states, which the application reads from the broker instead and so has no place for. */
type AnsweredThing = VosThing & { States?: string[] };

export interface FindingsAnswer {
  spec: string;
  scopeId: string;
  things: AnsweredThing[];
  relationships: VosRelationship[];
  ranges: Record<string, ThingRangesResponse>;
}

export interface Findings {
  /** The page the model declares, parsed. Not localized: which language it reads in changes while the
   *  page is open, and what it draws does not. */
  spec: DashboardSpec;
  index: ModelIndex;
  scopeId: string;
  reads: ModelReads;
}

/** Throws where the service answered a spec that is not one. Read here rather than while rendering, so
 *  a page that cannot be drawn says so where every other refusal is said, instead of drawing nothing —
 *  which is how a figure the analysis has not computed reads. */
export function findingsFrom(answer: FindingsAnswer): Findings {
  const spec = JSON.parse(answer.spec) as DashboardSpec;
  const relationships = answer.relationships.map(unwrapRelationship);
  const holders = statesByThing(answer.things);

  return {
    spec,
    index: buildModelIndex(answer.things.map(unwrapThing), relationships),
    scopeId: answer.scopeId,
    reads: {
      thingsInState: (state) => Promise.resolve(inState(state, answer.things, holders)),
      thingRanges: (thingId) => Promise.resolve(answer.ranges[thingId] ?? null),
      aggregate: () => Promise.reject(new Error('A findings page reads no history.')),
      fromService: () => Promise.reject(new Error('A findings page calls no service.')),
    },
  };
}

function statesByThing(things: AnsweredThing[]): Map<string, string[]> {
  return new Map(things.map((thing) => [thing.Id, thing.States ?? []]));
}

function inState(
  state: string,
  things: AnsweredThing[],
  holders: Map<string, string[]>,
): ThingsInStateResponse {
  return {
    StateName: state,
    Things: things
      .filter((thing) => holders.get(thing.Id)?.includes(state))
      .map((thing) => ({ Id: thing.Id, Name: thing.Name })),
  };
}
