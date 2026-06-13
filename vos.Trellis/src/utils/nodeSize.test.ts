import { describe, it, expect } from 'vitest';
import {
  computeNodeSize,
  NODE_SIZE_MIN,
  NODE_SIZE_MAX,
  NODE_SIZE_SLOPE,
} from './nodeSize';

describe('computeNodeSize (Bug #5361)', () => {
  it('returns NODE_SIZE_MIN for an isolated node (degree 0)', () => {
    expect(computeNodeSize(0)).toBe(NODE_SIZE_MIN);
  });

  it('grows linearly with degree below the cap', () => {
    // degree 5 × slope 0.4 + min 1 = 3
    expect(computeNodeSize(5)).toBeCloseTo(NODE_SIZE_MIN + 5 * NODE_SIZE_SLOPE, 6);
    // degree 10 × slope 0.4 + min 1 = 5
    expect(computeNodeSize(10)).toBeCloseTo(NODE_SIZE_MIN + 10 * NODE_SIZE_SLOPE, 6);
  });

  it('caps at NODE_SIZE_MAX for very-high-degree hubs', () => {
    expect(computeNodeSize(100)).toBe(NODE_SIZE_MAX);
    expect(computeNodeSize(10000)).toBe(NODE_SIZE_MAX);
  });

  it('finds the cap-crossover at the expected degree', () => {
    // Cap at 6 with slope 0.4 and min 1: 1 + d*0.4 = 6 → d = 12.5.
    // Anything at or above degree 13 should clamp.
    expect(computeNodeSize(12)).toBeLessThan(NODE_SIZE_MAX);
    expect(computeNodeSize(13)).toBe(NODE_SIZE_MAX);
  });

  it('never returns below NODE_SIZE_MIN even for negative input', () => {
    // Defensive — degree should never be negative, but the clamp must hold.
    expect(computeNodeSize(-5)).toBe(NODE_SIZE_MIN);
  });
});
