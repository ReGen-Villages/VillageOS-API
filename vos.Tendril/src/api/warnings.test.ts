import { describe, it, expect } from 'vitest';
import type { Binding } from '../types/dashboard';
import type { ModelReads } from './modelReads';
import { buildModelIndex, resolveBinding } from './dashboardApi';

const series: Extract<Binding, { kind: 'timeseries' }> = {
  kind: 'timeseries',
  archetype: 'Reservoir',
  happenedAt: 'measuredAt',
  reduction: 'average',
  bucketSeconds: 3600,
  buckets: 24,
};

async function warningsOf(binding: Binding): Promise<{ result: unknown; warnings: string[] }> {
  const warnings: string[] = [];
  const result = await resolveBinding(binding, {
    index: buildModelIndex([], []),
    scopeId: null,
    reads: {} as ModelReads,
    warn: (warning) => warnings.push(warning),
  });
  return { result, warnings };
}

describe('a question a page description asks that cannot be asked', () => {
  it('resolves to nothing and says a series cannot be narrowed to what reaches a container', async () => {
    const asked = await warningsOf({ ...series, scope: { viaPredicate: 'feeds', direction: 'in' } });

    expect(asked.result).toBeNull();
    expect(asked.warnings).toEqual([expect.stringContaining('timeseries over Reservoir')]);
  });

  it('resolves to nothing and says an average of several buckets cannot be folded up', async () => {
    const asked = await warningsOf({ ...series, bucketsPerPoint: 4 });

    expect(asked.result).toBeNull();
    expect(asked.warnings).toEqual([expect.stringContaining('average of several buckets')]);
  });

  it('resolves to nothing where nobody asked to be told', async () => {
    const result = await resolveBinding(
      { ...series, scope: { viaPredicate: 'feeds', direction: 'in' } },
      { index: buildModelIndex([], []), scopeId: null, reads: {} as ModelReads },
    );

    expect(result).toBeNull();
  });
});
