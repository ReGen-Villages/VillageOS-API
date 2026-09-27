import { describe, it, expect } from 'vitest';
import { computeNodeSize } from './nodeSize';
import { LAYOUT_DEFAULTS } from './guiSettings';

const { nodeSizeMin: min, nodeSizeMax: max, nodeSizeSlope: slope } = LAYOUT_DEFAULTS;

describe('computeNodeSize', () => {
  it('returns the minimum size for an isolated node', () => {
    expect(computeNodeSize(0)).toBe(min);
  });

  it('grows linearly with degree below the cap', () => {
    expect(computeNodeSize(5)).toBeCloseTo(min + 5 * slope, 6);
    expect(computeNodeSize(10)).toBeCloseTo(min + 10 * slope, 6);
  });

  it('caps at the maximum size for very-high-degree hubs', () => {
    expect(computeNodeSize(100)).toBe(max);
    expect(computeNodeSize(10000)).toBe(max);
  });

  it('crosses the cap at the degree the slope predicts', () => {
    const crossover = Math.ceil((max - min) / slope);
    expect(computeNodeSize(crossover - 1)).toBeLessThan(max);
    expect(computeNodeSize(crossover)).toBe(max);
  });

  it('never returns below the minimum even for negative input', () => {
    expect(computeNodeSize(-5)).toBe(min);
  });
});
