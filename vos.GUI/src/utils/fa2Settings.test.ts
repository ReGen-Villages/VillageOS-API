import { describe, it, expect } from 'vitest';
import {
  resolveFA2Settings,
  FA2_SCALING_RATIO_MULTIPLIER,
  FA2_GRAVITY_MULTIPLIER,
  FA2_SPREAD_GRAVITY_FACTOR,
  FA2_BARNES_HUT_THETA,
} from './fa2Settings';
import { LAYOUT_DEFAULTS } from './guiSettings';

describe('resolveFA2Settings (Bug #5338 + Bug #5361)', () => {
  it('produces the tuned defaults for the default LayoutSettings', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, /* isSpreadActive */ false);
    // scalingRatio: repulsion 0.1 × 500 = 50 (was 10 before Bug #5338).
    // The old value was visibly cramped on the 14k-node MV graph; kept at 50.
    expect(r.scalingRatio).toBe(50);
    // gravity: gravity 0.0001 × 10000 = 1.0. Bug #5338 lowered this to 0.3
    // (multiplier 3000) for spread, but the weak gravity made the 14k-node
    // graph slow to converge — Bug #5361 restored 10000 and recovered perf
    // via barnesHutTheta instead.
    expect(r.gravity).toBeCloseTo(1.0, 6);
  });

  it('uses the optimized Barnes-Hut theta for large graphs (Bug #5361)', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    expect(r.barnesHutOptimize).toBe(true);
    // Bug #5361 — bumped from 0.5 to 1.2 to recover ~2-3x per-iteration
    // speed on the 14k-node MV graph (Gephi default for optimized mode;
    // Jacomy 2014 benchmark setting).
    expect(r.barnesHutTheta).toBe(1.2);
    expect(r.barnesHutTheta).toBe(FA2_BARNES_HUT_THETA);
    expect(r.slowDown).toBe(5);
    expect(r.strongGravityMode).toBe(false);
  });

  it('reduces gravity by FA2_SPREAD_GRAVITY_FACTOR in spread mode', () => {
    const normal = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    const spread = resolveFA2Settings(LAYOUT_DEFAULTS, true);
    expect(spread.gravity).toBeCloseTo(normal.gravity * FA2_SPREAD_GRAVITY_FACTOR, 6);
    // scalingRatio is NOT affected by spread mode — that knob is for the
    // small-graph supervisor, not FA2.
    expect(spread.scalingRatio).toBe(normal.scalingRatio);
  });

  it('honors a user override on layoutSettings.repulsion (linear in the multiplier)', () => {
    const overridden = resolveFA2Settings({ ...LAYOUT_DEFAULTS, repulsion: 0.2 }, false);
    expect(overridden.scalingRatio).toBe(0.2 * FA2_SCALING_RATIO_MULTIPLIER);
  });

  it('honors a user override on layoutSettings.gravity (linear in the multiplier)', () => {
    const overridden = resolveFA2Settings({ ...LAYOUT_DEFAULTS, gravity: 0.0005 }, false);
    expect(overridden.gravity).toBeCloseTo(0.0005 * FA2_GRAVITY_MULTIPLIER, 6);
  });
});
