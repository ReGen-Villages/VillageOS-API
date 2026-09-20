/**
 * What a single figure on a dashboard is made of.
 *
 * A tile answers one question with one number. This answers the second question a reader has about
 * it — which Things it was formed from, and how — by asking the same narrowed question the figure
 * was formed from, for its members instead of its number, rather than resolving a second binding
 * written beside it. A count's breakdown *is* the list the count counted, so the list and the
 * figure above it cannot disagree.
 *
 * Nothing here names a domain. The words in {@link FigureTerms} are the model's own and reach the
 * reader untranslated, exactly as state names, archetypes and property keys do everywhere else.
 */
import { SCOPE_REF, type Binding, type PropertyFilter } from '../types/dashboard';
import {
  type ResolveContext,
  type Row,
  aggregateMembers,
  aggregateValue,
  asNumber,
  asRows,
  asSeries,
  divide,
  referencedThing,
  resolveBinding,
  rowOfThing,
  thingsOfArchetype,
} from './dashboardApi';

/** How the rows became the figure. `read` is the one that reduces nothing: the figure is a value a
 *  Thing already carries, and the row behind it is that Thing. */
export type Reduction = 'count' | 'sum' | 'avg' | 'min' | 'max' | 'read';

/** The model's own words for what a figure names, carried to the reader as the model wrote them. */
export interface FigureTerms {
  archetype?: string;
  state?: string;
  property?: string;
  /** The instant property a trailing-window figure reads each member's event from. */
  happenedAt?: string;
  within?: string;
  where?: PropertyFilter[];
}

export type BehindTheFigure =
  | { kind: 'things'; reduction: Reduction; rows: Row[]; measure: string | null }
  | { kind: 'division'; numerator: FigureBreakdown; denominator: FigureBreakdown }
  /** A reduction the platform performed over time. There are no rows behind it — what there is is
   *  the same question over the same window, asked in parts. `endsAt` is the moment the answer came
   *  back, which is where the trailing window ends and what the parts are labelled from. */
  | {
      kind: 'buckets';
      reduction: Reduction;
      values: number[];
      bucketSeconds: number;
      windowSeconds: number;
      endsAt: number;
    };

export interface FigureBreakdown {
  value: number | null;
  terms: FigureTerms;
  /** Null for a figure with nothing to open: a constant, or a side of a division that is one. */
  behind: BehindTheFigure | null;
}

/** Whether a figure has anything behind it to open. Answered from the binding's shape alone, so a
 *  tile draws the mark that says so without asking the platform anything. */
export function hasBreakdown(binding: Binding | undefined): boolean {
  if (!binding) return false;
  switch (binding.kind) {
    case 'stateCount':
    case 'aggregate':
    case 'property':
      return true;
    case 'ratio':
      return hasBreakdown(binding.numerator) || hasBreakdown(binding.denominator);
    case 'latest':
      // What is behind the figure is the buckets its point was added from; a point that is one
      // bucket has no finer question to ask.
      return (binding.series.bucketsPerPoint ?? 1) > 1;
    default:
      // A trace is already the detail behind a tile; a constant and a service's answer have no
      // Things the console can list.
      return false;
  }
}

/** What one figure is made of, or null when it is made of nothing a reader can be shown. */
export async function breakdownOf(binding: Binding, ctx: ResolveContext): Promise<FigureBreakdown | null> {
  const selected = ctx.scopeId ? ctx.idx.byId.get(ctx.scopeId)?.Name : undefined;

  switch (binding.kind) {
    case 'const':
      return { value: binding.value, terms: {}, behind: null };

    case 'stateCount': {
      // The count's own narrowing, asked for its rows: the state read narrows a list exactly as it
      // narrows a count, so the members listed are the members counted.
      const counted = asRows(
        await resolveBinding({ kind: 'stateList', state: binding.state, scope: binding.scope, archetype: binding.archetype, excludeState: binding.excludeState }, ctx),
      );
      return {
        value: counted.length,
        terms: { archetype: binding.archetype, state: binding.state, within: binding.scope ? selected : undefined },
        behind: {
          kind: 'things',
          reduction: 'count',
          measure: null,
          rows: counted.map((row) => rowOfThing(String(row.id), String(row.name), ctx)),
        },
      };
    }

    case 'aggregate': {
      const members = aggregateMembers(binding, ctx);
      const measure = binding.op === 'count' ? null : (binding.property ?? null);
      return {
        value: aggregateValue(binding, members, ctx),
        terms: {
          archetype: binding.archetype,
          property: measure ?? undefined,
          within: binding.scope ? selected : undefined,
          where: binding.where,
        },
        behind: {
          kind: 'things',
          reduction: binding.op,
          measure,
          rows: members.map((thing) => rowOfThing(thing.Id, thing.Name, ctx)),
        },
      };
    }

    case 'property': {
      const named = referencedThing(binding.thing, ctx);
      // With nothing selected, `$scope` reads as the average across the compared entities — so the
      // rows behind such a figure are those entities, each with the value it contributed.
      const averaged =
        !named && binding.thing === SCOPE_REF && ctx.compareArchetype
          ? thingsOfArchetype(ctx.compareArchetype, ctx.idx)
          : [];
      const holders = named ? [named] : averaged;
      if (!holders.length) return null;
      return {
        value: asNumber(await resolveBinding(binding, ctx)),
        terms: { property: binding.property, archetype: averaged.length ? ctx.compareArchetype : undefined },
        behind: {
          kind: 'things',
          reduction: averaged.length ? 'avg' : 'read',
          measure: binding.property,
          rows: holders.map((thing) => rowOfThing(thing.Id, thing.Name, ctx)),
        },
      };
    }

    case 'ratio': {
      const [numerator, denominator] = await Promise.all([
        breakdownOf(binding.numerator, ctx),
        breakdownOf(binding.denominator, ctx),
      ]);
      if (!numerator || !denominator) return null;
      return {
        value: divide(numerator.value, denominator.value),
        terms: {},
        behind: numerator.behind || denominator.behind ? { kind: 'division', numerator, denominator } : null,
      };
    }

    case 'latest': {
      const series = binding.series;
      const bucketsPerPoint = series.bucketsPerPoint ?? 1;
      const [figure, parts] = await Promise.all([
        resolveBinding(binding, ctx),
        resolveBinding({ ...series, buckets: bucketsPerPoint, bucketsPerPoint: 1 }, ctx),
      ]);
      const values = asSeries(parts);
      return {
        value: asNumber(figure),
        terms: { archetype: series.archetype, property: series.property, happenedAt: series.happenedAt },
        behind: values.length
          ? {
              kind: 'buckets',
              reduction: series.op,
              values,
              bucketSeconds: series.bucketSeconds,
              windowSeconds: series.bucketSeconds * bucketsPerPoint,
              endsAt: Date.now(),
            }
          : null,
      };
    }

    default:
      return null;
  }
}
