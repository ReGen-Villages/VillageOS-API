import type { LayoutSettings } from './guiSettings';

/**
 * Multiplier from <c>layoutSettings.repulsion</c> (a small per-pair force coefficient
 * used by the small-graph force layout) to ForceAtlas2's <c>scalingRatio</c>
 * (its global node-spacing knob). Larger values push nodes further apart.
 *
 * Bumped from 100 → 500 in Bug #5338. Default <c>repulsion = 0.1</c> now yields
 * scalingRatio = 50 (was 10), which gives the 14k-node MV graph room to breathe
 * without making small graphs explode (the small-graph path uses a different
 * supervisor and ignores this multiplier).
 */
export const FA2_SCALING_RATIO_MULTIPLIER = 500;

/**
 * Multiplier from <c>layoutSettings.gravity</c> (a tiny anchor force) to ForceAtlas2's
 * <c>gravity</c> (its centering knob). Lower values let nodes spread further from
 * the origin instead of being pulled into the centre.
 *
 * Restored to 10000 in Bug #5361. Bug #5338 had bumped this down to 3000, which
 * gave nice spread but left the centering force so weak that the 14k-node MV
 * graph took many more iterations to converge. The dominant convergence
 * problem was the weak gravity, not the higher repulsion — keeping
 * scalingRatio at 500 preserves the breathing room without the perf hit.
 */
export const FA2_GRAVITY_MULTIPLIER = 10000;

/** Spread mode (<c>isSpreadActive</c>) reduces gravity by this factor for extra breathing room. */
export const FA2_SPREAD_GRAVITY_FACTOR = 0.1;

/**
 * Barnes-Hut tree-traversal cutoff. At each tree region, FA2 asks whether
 * <c>region_size / distance &lt; theta</c>; if so, the whole region is
 * approximated as a single point at its centroid. Lower theta = more
 * recursion = more accurate. Higher = faster.
 *
 * Bumped from 0.5 → 1.2 in Bug #5361. 1.2 is the value Jacomy 2014 (FA2
 * paper) used for >10k-node benchmarks and Gephi's default for "optimized"
 * mode. Per-iteration cost typically drops 2-3x. The cost is sub-pixel
 * layout precision and slightly less crisp dense-cluster boundaries —
 * invisible at 14k-node scale.
 */
export const FA2_BARNES_HUT_THETA = 1.2;

export interface ResolvedFA2Settings {
  scalingRatio: number;
  gravity: number;
  barnesHutOptimize: boolean;
  barnesHutTheta: number;
  slowDown: number;
  strongGravityMode: boolean;
}

/**
 * Pure function (Bug #5338) — derive the ForceAtlas2 supervisor settings from
 * the user-tunable {@link LayoutSettings} plus the runtime spread-mode flag.
 * Extracted from <c>LayoutController</c> so the tuning is unit-testable.
 */
export function resolveFA2Settings(
  layoutSettings: LayoutSettings,
  isSpreadActive: boolean,
): ResolvedFA2Settings {
  const baseGravity = layoutSettings.gravity * FA2_GRAVITY_MULTIPLIER;
  const gravity = isSpreadActive ? baseGravity * FA2_SPREAD_GRAVITY_FACTOR : baseGravity;
  return {
    scalingRatio: layoutSettings.repulsion * FA2_SCALING_RATIO_MULTIPLIER,
    gravity,
    barnesHutOptimize: true,
    barnesHutTheta: FA2_BARNES_HUT_THETA,
    slowDown: 5,
    strongGravityMode: false,
  };
}
