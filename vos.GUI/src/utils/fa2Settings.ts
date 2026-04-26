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
 * Bumped down from 10000 → 3000 in Bug #5338. Default <c>gravity = 0.0001</c> now
 * yields FA2 gravity = 0.3 (was 1.0), reducing the centering pressure that was
 * compressing dense clusters.
 */
export const FA2_GRAVITY_MULTIPLIER = 3000;

/** Spread mode (<c>isSpreadActive</c>) reduces gravity by this factor for extra breathing room. */
export const FA2_SPREAD_GRAVITY_FACTOR = 0.1;

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
    barnesHutTheta: 0.5,
    slowDown: 5,
    strongGravityMode: false,
  };
}
