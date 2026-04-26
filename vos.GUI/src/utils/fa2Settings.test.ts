import { describe, it, expect } from 'vitest';
import { resolveFA2Settings, FA2_SCALING_RATIO_MULTIPLIER, FA2_GRAVITY_MULTIPLIER, FA2_SPREAD_GRAVITY_FACTOR } from './fa2Settings';
import { LAYOUT_DEFAULTS } from './guiSettings';

describe('resolveFA2Settings (Bug #5338)', () => {
  it('produces the new tuned defaults for the default LayoutSettings', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, /* isSpreadActive */ false);
    // scalingRatio: repulsion 0.1 × 500 = 50 (was 10 before the bump).
    // The old value was visibly cramped on the 14k-node MV graph.
    expect(r.scalingRatio).toBe(50);
    // gravity: gravity 0.0001 × 3000 = 0.3 (was 1.0). Less centering pressure
    // gives dense clusters room to fan out.
    expect(r.gravity).toBeCloseTo(0.3, 6);
  });

  it('keeps the Barnes-Hut + slowDown defaults that worked pre-tune', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    expect(r.barnesHutOptimize).toBe(true);
    expect(r.barnesHutTheta).toBe(0.5);
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
