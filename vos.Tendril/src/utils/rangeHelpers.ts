import type { RangeDto, ThingRangesResponse, InheritedRangeSetDto } from '../types/vos';

/** Compute badge color from a range's binding deviations. */
export function rangeBindingColor(range: RangeDto | undefined): 'green' | 'yellow' | 'red' {
  if (!range?.Bindings?.length) return 'green';

  const outOfBounds = range.Bindings.filter((b) => b.IsActive && b.IsInBounds === false);
  if (outOfBounds.length === 0) return 'green';

  // A categorical or pattern range has no severity and counts as 1, fully out of bounds and red;
  // a numeric severity runs from 0 to 1 across the bound's range.
  const maxSeverity = Math.max(...outOfBounds.map((b) => b.DeviationSeverity ?? 1));
  return maxSeverity >= 0.5 ? 'red' : 'yellow';
}

/** Find a range by name across own and inherited ranges. */
export function findRange(name: string, data: ThingRangesResponse): RangeDto | undefined {
  const own = data.OwnRanges.find((r) => r.Name === name);
  if (own) return own;

  function walk(sets: InheritedRangeSetDto[]): RangeDto | undefined {
    for (const s of sets) {
      const found = s.Ranges.find((r) => r.Name === name);
      if (found) return found;
      const nested = walk(s.Inherited);
      if (nested) return nested;
    }
    return undefined;
  }
  return walk(data.InheritedRanges);
}

/** Determine badge color for a state based on its binding deviations. */
export function stateColor(stateName: string, rangesData: ThingRangesResponse | null): 'green' | 'yellow' | 'red' {
  if (!rangesData) return 'green';
  const range = findRange(stateName, rangesData);
  return rangeBindingColor(range);
}
