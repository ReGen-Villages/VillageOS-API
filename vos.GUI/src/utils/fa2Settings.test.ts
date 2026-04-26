import { describe, it, expect } from 'vitest';
import {
  resolveFA2Settings,
  FA2_SCALING_RATIO_MULTIPLIER,
  FA2_GRAVITY_MULTIPLIER,
  FA2_SPREAD_GRAVITY_FACTOR,
  FA2_BARNES_HUT_THETA,
  FA2_SLOW_DOWN,
  FA2_STRONG_GRAVITY_MODE,
} from './fa2Settings';
import { LAYOUT_DEFAULTS } from './guiSettings';

describe('resolveFA2Settings (Bug #5338 + Bug #5361)', () => {
  it('produces the empirically-validated defaults (Bug #5361)', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, /* isSpreadActive */ false);
    // scalingRatio: repulsion 0.1 × 100 = 10. Bug #5338 raised this to 50 for
    // spread on the then-14k-node graph, but Bug #5358 (material composites)
    // doubled the count to ~30k and at that scale 50 produces unbounded
    // expansion that gravity cannot counteract. Live-browser test confirmed
    // 10 converges in ~12s; 50 never converges.
    expect(r.scalingRatio).toBe(10);
    // gravity: gravity 0.0001 × 10000 = 1.0. Combined with strongGravityMode
    // this is the only setting that holds 30k nodes in a stable disc.
    expect(r.gravity).toBeCloseTo(1.0, 6);
  });

  it('uses Barnes-Hut + strongGravityMode + damping for large-graph convergence (Bug #5361)', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    expect(r.barnesHutOptimize).toBe(true);
    // theta 1.2 (Gephi default for optimized mode; Jacomy 2014 >10k benchmark
    // setting). Per-iteration cost drops 2-3x vs the prior 0.5.
    expect(r.barnesHutTheta).toBe(1.2);
    expect(r.barnesHutTheta).toBe(FA2_BARNES_HUT_THETA);
    // slowDown 10 (was 5) — extra damping so the strong-gravity contraction
    // phase doesn't overshoot equilibrium and oscillate.
    expect(r.slowDown).toBe(10);
    expect(r.slowDown).toBe(FA2_SLOW_DOWN);
    // strongGravityMode true — gravity scales linearly with distance instead
    // of inversely. THE key change: at 30k nodes, normal-mode gravity is too
    // weak at large bounding-box widths to hold the graph against repulsion.
    expect(r.strongGravityMode).toBe(true);
    expect(r.strongGravityMode).toBe(FA2_STRONG_GRAVITY_MODE);
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
