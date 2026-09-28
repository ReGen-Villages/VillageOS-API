import { describe, it, expect } from 'vitest';
import { resolveFA2Settings, FA2_SPREAD_GRAVITY_FACTOR } from './fa2Settings';
import { LAYOUT_DEFAULTS } from './guiSettings';

describe('resolveFA2Settings', () => {
  it('produces the validated defaults', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    // A scaling ratio of 10 converges on a graph of tens of thousands of nodes in
    // about twelve seconds; 50 expands without bound because gravity cannot counteract it.
    expect(r.scalingRatio).toBe(10);
    // Gravity of 1.0 with strong-gravity mode is the only setting that holds such a
    // graph in a stable disc.
    expect(r.gravity).toBeCloseTo(1.0, 6);
  });

  it('uses Barnes-Hut, strong gravity and damping for large-graph convergence', () => {
    const r = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    expect(r.barnesHutOptimize).toBe(true);
    expect(r.barnesHutTheta).toBe(LAYOUT_DEFAULTS.barnesHutTheta);
    expect(r.slowDown).toBe(LAYOUT_DEFAULTS.slowDown);
    expect(r.strongGravityMode).toBe(LAYOUT_DEFAULTS.strongGravityMode);
  });

  it('reduces gravity by the spread factor in spread mode and leaves the scaling ratio alone', () => {
    const normal = resolveFA2Settings(LAYOUT_DEFAULTS, false);
    const spread = resolveFA2Settings(LAYOUT_DEFAULTS, true);
    expect(spread.gravity).toBeCloseTo(normal.gravity * FA2_SPREAD_GRAVITY_FACTOR, 6);
    expect(spread.scalingRatio).toBe(normal.scalingRatio);
  });

  it('honors a user override on repulsion, linear in the multiplier', () => {
    const overridden = resolveFA2Settings({ ...LAYOUT_DEFAULTS, repulsion: 0.2 }, false);
    expect(overridden.scalingRatio).toBe(0.2 * LAYOUT_DEFAULTS.scalingRatioMultiplier);
  });

  it('honors a user override on gravity, linear in the multiplier', () => {
    const overridden = resolveFA2Settings({ ...LAYOUT_DEFAULTS, gravity: 0.0005 }, false);
    expect(overridden.gravity).toBeCloseTo(0.0005 * LAYOUT_DEFAULTS.gravityMultiplier, 6);
  });

  it('derives the scaling ratio from clusterRepulsion when clustering is active', () => {
    const normal = resolveFA2Settings(LAYOUT_DEFAULTS, false, false);
    const clustering = resolveFA2Settings(LAYOUT_DEFAULTS, false, true);
    expect(normal.scalingRatio).toBe(LAYOUT_DEFAULTS.repulsion * LAYOUT_DEFAULTS.scalingRatioMultiplier);
    expect(clustering.scalingRatio).toBe(LAYOUT_DEFAULTS.clusterRepulsion * LAYOUT_DEFAULTS.scalingRatioMultiplier);
  });

  it('leaves gravity untouched by the clustering flag', () => {
    const normal = resolveFA2Settings(LAYOUT_DEFAULTS, false, false);
    const clustering = resolveFA2Settings(LAYOUT_DEFAULTS, false, true);
    expect(clustering.gravity).toBeCloseTo(normal.gravity, 6);
  });
});
