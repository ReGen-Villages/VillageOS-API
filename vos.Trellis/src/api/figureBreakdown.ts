/**
 * Which Things a figure was formed from, found by asking the figure's own question for its members rather
 * than through a second binding, so the list and the figure above it cannot disagree. The words in
 * {@link FigureTerms} are the model's own and reach the reader untranslated, as state names do everywhere.
 */
import { SCOPE_REFERENCE, type Binding, type PropertyFilter } from '@tendril-ui/tendril';
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
} from '@tendril-ui/tendril';

/** `read` reduces nothing: the figure is a value one Thing carries, and that Thing is the row behind it. */
type Reduction = 'count' | 'sum' | 'average' | 'min' | 'max' | 'read';

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
  /** A reduction over time has no rows behind it, only the same question over the same window in parts.
   *  `endsAt` is when the answer came back, where the window ends and what the parts are labelled from. */
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

/** From the binding's shape alone, so a tile draws its mark without asking the platform anything. */
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
      // Behind the figure are the buckets its point was added from, and a one-bucket point has none finer.
      return (binding.series.bucketsPerPoint ?? 1) > 1;
    default:
      // A trace is already the detail behind a tile; a constant and a service's answer have no
      // Things the console can list.
      return false;
  }
}

export async function breakdownOf(binding: Binding, context: ResolveContext): Promise<FigureBreakdown | null> {
  const selected = context.scopeId ? context.index.byId.get(context.scopeId)?.Name : undefined;

  switch (binding.kind) {
    case 'const':
      return { value: binding.value, terms: {}, behind: null };

    case 'stateCount': {
      // The state read narrows a list exactly as it narrows a count, so the members listed are those counted.
      const counted = asRows(
        await resolveBinding({ kind: 'stateList', state: binding.state, scope: binding.scope, archetype: binding.archetype, excludeState: binding.excludeState }, context),
      );
      return {
        value: counted.length,
        terms: { archetype: binding.archetype, state: binding.state, within: binding.scope ? selected : undefined },
        behind: {
          kind: 'things',
          reduction: 'count',
          measure: null,
          rows: counted.map((row) => rowOfThing(String(row.id), String(row.name), context)),
        },
      };
    }

    case 'aggregate': {
      const members = aggregateMembers(binding, context);
      const measure = binding.reduction === 'count' ? null : (binding.property ?? null);
      return {
        value: aggregateValue(binding, members, context),
        terms: {
          archetype: binding.archetype,
          property: measure ?? undefined,
          within: binding.scope ? selected : undefined,
          where: binding.where,
        },
        behind: {
          kind: 'things',
          reduction: binding.reduction,
          measure,
          rows: members.map((thing) => rowOfThing(thing.Id, thing.Name, context)),
        },
      };
    }

    case 'property': {
      const named = referencedThing(binding.thing, context);
      // With nothing selected, `$scope` reads as the average across the compared entities, so those are the rows.
      const averaged =
        !named && binding.thing === SCOPE_REFERENCE && context.compareArchetype
          ? thingsOfArchetype(context.compareArchetype, context.index)
          : [];
      const holders = named ? [named] : averaged;
      if (!holders.length) return null;
      return {
        value: asNumber(await resolveBinding(binding, context)),
        terms: { property: binding.property, archetype: averaged.length ? context.compareArchetype : undefined },
        behind: {
          kind: 'things',
          reduction: averaged.length ? 'average' : 'read',
          measure: binding.property,
          rows: holders.map((thing) => rowOfThing(thing.Id, thing.Name, context)),
        },
      };
    }

    case 'ratio': {
      const [numerator, denominator] = await Promise.all([
        breakdownOf(binding.numerator, context),
        breakdownOf(binding.denominator, context),
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
        resolveBinding(binding, context),
        resolveBinding({ ...series, buckets: bucketsPerPoint, bucketsPerPoint: 1 }, context),
      ]);
      const values = asSeries(parts);
      return {
        value: asNumber(figure),
        terms: { archetype: series.archetype, property: series.property, happenedAt: series.happenedAt },
        behind: values.length
          ? {
              kind: 'buckets',
              reduction: series.reduction,
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
