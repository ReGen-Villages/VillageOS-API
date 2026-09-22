import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import type { Binding } from '../types/dashboard';
import type { ModelReads } from './modelReads';
import { buildModelIndex, type ResolveContext, type Row } from './dashboardApi';
import { breakdownOf, hasBreakdown } from './figureBreakdown';

/** Stamp the archetype declaration a real model carries on everything something `is`. */
function declared(things: VosThing[], relationships: VosRelationship[]): VosThing[] {
  const targets = new Set(relationships.filter((r) => r.PredicateId === 'is').map((r) => r.TargetId));
  return things.map((x) => (targets.has(x.Id) ? { ...x, IsArchetype: true } : x));
}

const THINGS: VosThing[] = [
  { Id: 'is', Name: 'is', Properties: {} },
  { Id: 'holds', Name: 'holds', Properties: {} },
  { Id: 'arch-site', Name: 'Site', Properties: {} },
  { Id: 'arch-catchment', Name: 'Catchment', Properties: {} },
  { Id: 'site-1', Name: 'North ridge', Properties: { self_sufficiency_rate: 98.4 } },
  { Id: 'site-2', Name: 'South valley', Properties: { self_sufficiency_rate: 95.6 } },
  { Id: 'catchment-1', Name: 'CATCH-1', Properties: { area: 100, stored: 40, spring_fed: true } },
  { Id: 'catchment-2', Name: 'CATCH-2', Properties: { area: 300, stored: 200, spring_fed: false } },
];

const RELATIONSHIPS: VosRelationship[] = [
  ['site-1', 'arch-site'],
  ['site-2', 'arch-site'],
  ['catchment-1', 'arch-catchment'],
  ['catchment-2', 'arch-catchment'],
  ['site-1', 'catchment-1'],
].map(([SubjectId, TargetId], i) => ({
  Id: `r${i}`,
  Name: `${SubjectId} → ${TargetId}`,
  SubjectId,
  PredicateId: TargetId.startsWith('arch') ? 'is' : 'holds',
  TargetId,
  Properties: {},
}));

const reads = {
  thingsInState: vi.fn(),
  thingRanges: vi.fn(),
  aggregate: vi.fn(),
  fromService: vi.fn(),
};

function context(scopeId: string | null = null): ResolveContext {
  return {
    index: buildModelIndex(declared(THINGS, RELATIONSHIPS), RELATIONSHIPS),
    scopeId,
    compareArchetype: 'Site',
    reads: reads as unknown as ModelReads,
  };
}

function names(rows: Row[]): string[] {
  return rows.map((r) => String(r.name)).sort();
}

beforeEach(() => {
  reads.thingsInState.mockReset();
  reads.aggregate.mockReset();
});

describe('which figures open at all', () => {
  it.each([
    ['stateCount', { kind: 'stateCount', state: 'flooded' }],
    ['aggregate', { kind: 'aggregate', archetype: 'Catchment', op: 'count' }],
    ['property', { kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }],
  ])('a %s figure names the Things behind it', (_kind, binding) => {
    expect(hasBreakdown(binding as Binding)).toBe(true);
  });

  it('a constant has nothing behind it', () => {
    expect(hasBreakdown({ kind: 'const', value: 12 })).toBe(false);
  });

  it("a service's answer has nothing the console can list", () => {
    expect(hasBreakdown({ kind: 'service', endpoint: 'metrics' })).toBe(false);
  });

  it('a ratio opens when either side does', () => {
    expect(hasBreakdown({
      kind: 'ratio',
      numerator: { kind: 'aggregate', archetype: 'Catchment', op: 'sum', property: 'stored' },
      denominator: { kind: 'const', value: 1000 },
    })).toBe(true);
    expect(hasBreakdown({
      kind: 'ratio',
      numerator: { kind: 'const', value: 1 },
      denominator: { kind: 'const', value: 2 },
    })).toBe(false);
  });

  it('a point opens on the buckets it was added from, and not when it is one bucket', () => {
    const point = (bucketsPerPoint: number): Binding => ({
      kind: 'latest',
      series: { kind: 'timeseries', archetype: 'Reading', happenedAt: 'taken_at', op: 'count', bucketSeconds: 900, buckets: 8, bucketsPerPoint },
    });
    expect(hasBreakdown(point(4))).toBe(true);
    expect(hasBreakdown(point(1))).toBe(false);
  });

  it('a trace is already its own detail and does not open', () => {
    expect(hasBreakdown({ kind: 'timeseries', archetype: 'Reading', happenedAt: 'taken_at', op: 'count', bucketSeconds: 3600, buckets: 8 })).toBe(false);
  });

  it('nothing to bind opens nothing', () => {
    expect(hasBreakdown(undefined)).toBe(false);
  });
});

describe('a state count opens to the Things it counted', () => {
  it('asks the state read for the members under the count’s own narrowing', async () => {
    reads.thingsInState.mockResolvedValue({
      StateName: 'flooded',
      Things: [{ Id: 'catchment-1', Name: 'CATCH-1' }, { Id: 'catchment-2', Name: 'CATCH-2' }],
    });

    const breakdown = await breakdownOf(
      { kind: 'stateCount', state: 'flooded', archetype: 'Catchment', scope: { viaPredicate: 'holds' } },
      context('site-1'),
    );

    expect(reads.thingsInState).toHaveBeenCalledWith('flooded', expect.objectContaining({ type: 'Catchment', within: 'site-1', withinPredicate: 'holds' }));
    expect(breakdown?.value).toBe(2);
    expect(breakdown?.behind).toMatchObject({ kind: 'things', reduction: 'count', measure: null });
    expect(names((breakdown?.behind as { rows: Row[] }).rows)).toEqual(['CATCH-1', 'CATCH-2']);
    expect(breakdown?.terms).toMatchObject({ state: 'flooded', archetype: 'Catchment', within: 'North ridge' });
  });

  it('asks the list read with the exclusion the count used, so the rows are the rows counted', async () => {
    reads.thingsInState.mockResolvedValue({ StateName: 'submitted', Things: [{ Id: 'catchment-1', Name: 'CATCH-1' }] });

    await breakdownOf({ kind: 'stateCount', state: 'submitted', excludeState: 'reviewed' }, context());

    expect(reads.thingsInState).toHaveBeenCalledWith('submitted', expect.objectContaining({ notIn: ['reviewed'] }));
  });

  it('carries the properties a counted Thing holds when the page holds the Thing', async () => {
    reads.thingsInState.mockResolvedValue({ StateName: 'flooded', Things: [{ Id: 'catchment-1', Name: 'CATCH-1' }] });

    const breakdown = await breakdownOf({ kind: 'stateCount', state: 'flooded' }, context());

    expect((breakdown?.behind as { rows: Row[] }).rows[0]).toMatchObject({ area: 100, stored: 40 });
  });
});

describe('a reduction opens to the members it reduced', () => {
  it('names the measure each member contributed', async () => {
    const breakdown = await breakdownOf({ kind: 'aggregate', archetype: 'Catchment', op: 'sum', property: 'stored' }, context());

    expect(breakdown?.value).toBe(240);
    expect(breakdown?.behind).toMatchObject({ kind: 'things', reduction: 'sum', measure: 'stored' });
    expect(names((breakdown?.behind as { rows: Row[] }).rows)).toEqual(['CATCH-1', 'CATCH-2']);
  });

  it('lists only the members the filters kept, and says which filters they were', async () => {
    const where = [{ property: 'spring_fed', op: '=' as const, value: true }];
    const breakdown = await breakdownOf({ kind: 'aggregate', archetype: 'Catchment', op: 'sum', property: 'area', where }, context());

    expect(breakdown?.value).toBe(100);
    expect(names((breakdown?.behind as { rows: Row[] }).rows)).toEqual(['CATCH-1']);
    expect(breakdown?.terms.where).toEqual(where);
  });

  it('a count has no measure column to draw', async () => {
    const breakdown = await breakdownOf({ kind: 'aggregate', archetype: 'Catchment', op: 'count' }, context());

    expect(breakdown?.value).toBe(2);
    expect(breakdown?.behind).toMatchObject({ reduction: 'count', measure: null });
  });
});

describe('a property figure opens to the Thing that carries it', () => {
  it('names the selected entity when one is selected', async () => {
    const breakdown = await breakdownOf({ kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }, context('site-1'));

    expect(breakdown?.value).toBe(98.4);
    expect(breakdown?.behind).toMatchObject({ kind: 'things', reduction: 'read', measure: 'self_sufficiency_rate' });
    expect(names((breakdown?.behind as { rows: Row[] }).rows)).toEqual(['North ridge']);
  });

  it('names every compared entity when the figure is their average', async () => {
    const breakdown = await breakdownOf({ kind: 'property', thing: '$scope', property: 'self_sufficiency_rate' }, context(null));

    expect(breakdown?.value).toBeCloseTo(97);
    expect(breakdown?.behind).toMatchObject({ reduction: 'avg' });
    expect(names((breakdown?.behind as { rows: Row[] }).rows)).toEqual(['North ridge', 'South valley']);
  });

  it('a figure read from a Thing the model has not got opens to nothing', async () => {
    expect(await breakdownOf({ kind: 'property', thing: 'Nowhere', property: 'x' }, context())).toBeNull();
  });
});

describe('a ratio opens to both of its sides', () => {
  const RATIO: Binding = {
    kind: 'ratio',
    numerator: { kind: 'aggregate', archetype: 'Catchment', op: 'sum', property: 'stored' },
    denominator: { kind: 'aggregate', archetype: 'Catchment', op: 'sum', property: 'area' },
  };

  it('divides one side by the other and keeps each side’s own rows', async () => {
    const breakdown = await breakdownOf(RATIO, context());
    const behind = breakdown?.behind as { kind: string; numerator: { value: number }; denominator: { value: number } };

    expect(breakdown?.value).toBeCloseTo(0.6);
    expect(behind.kind).toBe('division');
    expect(behind.numerator.value).toBe(240);
    expect(behind.denominator.value).toBe(400);
  });

  it('a side that is a constant carries its value and nothing to list', async () => {
    const breakdown = await breakdownOf({ ...RATIO, denominator: { kind: 'const', value: 400 } } as Binding, context());
    const behind = breakdown?.behind as { denominator: { value: number; behind: unknown } };

    expect(breakdown?.value).toBeCloseTo(0.6);
    expect(behind.denominator.value).toBe(400);
    expect(behind.denominator.behind).toBeNull();
  });
});

describe('a trailing-window figure opens to the buckets it was added from', () => {
  const TILE: Binding = {
    kind: 'latest',
    series: { kind: 'timeseries', archetype: 'Reading', happenedAt: 'taken_at', property: 'litres', op: 'sum', bucketSeconds: 900, buckets: 4, bucketsPerPoint: 4 },
  };

  it('asks for the buckets of the window its number covers', async () => {
    reads.aggregate.mockImplementation(async (query: { windowSeconds: number; bucketSeconds: number }) => ({
      Buckets: query.windowSeconds === 3600 ? [10, 20, 30, 40] : [1, 2, 3, 4, 5, 6, 7],
      FirstBucketStart: '2026-08-23T09:00:00Z',
      BucketSeconds: query.bucketSeconds,
      UnusableMembers: 0,
    }));

    const breakdown = await breakdownOf(TILE, context());

    expect(breakdown?.value).toBe(22);
    expect(breakdown?.behind).toMatchObject({ kind: 'buckets', bucketSeconds: 900, windowSeconds: 3600, reduction: 'sum' });
    expect((breakdown?.behind as { values: number[] }).values).toEqual([10, 20, 30, 40]);
    expect(breakdown?.terms).toMatchObject({ archetype: 'Reading', property: 'litres', happenedAt: 'taken_at' });
  });

  it('opens to nothing when the platform refuses the finer question', async () => {
    reads.aggregate.mockImplementation(async (query: { windowSeconds: number }) =>
      query.windowSeconds === 3600
        ? Promise.reject(new Error('refused'))
        : { Buckets: [1, 2, 3, 4, 5, 6, 7], FirstBucketStart: '', BucketSeconds: 900, UnusableMembers: 0 },
    );

    const breakdown = await breakdownOf(TILE, context());

    expect(breakdown?.behind).toBeNull();
  });
});

describe('figures with nothing behind them', () => {
  it('a constant carries its value and nothing to open', async () => {
    expect(await breakdownOf({ kind: 'const', value: 7 }, context())).toEqual({ value: 7, terms: {}, behind: null });
  });

  it("a service's answer opens to nothing", async () => {
    expect(await breakdownOf({ kind: 'service', endpoint: 'metrics' }, context())).toBeNull();
  });
});
