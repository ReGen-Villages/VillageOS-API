import type { LayoutSettings } from './guiSettings';
import { LAYOUT_DEFAULTS } from './guiSettings';

/**
 * Multiplier from <c>layoutSettings.repulsion</c> (a small per-pair force coefficient
 * used by the small-graph force layout) to ForceAtlas2's <c>scalingRatio</c>.
 *
 * Re-exported for back-compat with older callers (e.g. tests) that imported the
 * constant directly. The runtime path now reads <c>layoutSettings.scalingRatioMultiplier</c>
 * so it can be tuned through GUI_Settings without a code change (Bug #5361).
 */
export const FA2_SCALING_RATIO_MULTIPLIER = LAYOUT_DEFAULTS.scalingRatioMultiplier;

/**
 * Multiplier from <c>layoutSettings.gravity</c> to ForceAtlas2's <c>gravity</c>.
 * Runtime path uses <c>layoutSettings.gravityMultiplier</c>.
 */
export const FA2_GRAVITY_MULTIPLIER = LAYOUT_DEFAULTS.gravityMultiplier;

/** Spread mode (<c>isSpreadActive</c>) reduces gravity by this factor for extra breathing room. */
export const FA2_SPREAD_GRAVITY_FACTOR = 0.1;

/**
 * Barnes-Hut tree-traversal cutoff. Lower theta = more recursion = more accurate.
 * Higher = faster. Runtime path reads <c>layoutSettings.barnesHutTheta</c>.
 *
 * 1.2 (default) is the value Jacomy 2014 (FA2 paper) used for >10k-node
 * benchmarks and Gephi's default for "optimized" mode.
 */
export const FA2_BARNES_HUT_THETA = LAYOUT_DEFAULTS.barnesHutTheta;

/** Damping factor — runtime path reads <c>layoutSettings.slowDown</c>. */
export const FA2_SLOW_DOWN = LAYOUT_DEFAULTS.slowDown;

/**
 * Strong-gravity mode — runtime path reads <c>layoutSettings.strongGravityMode</c>.
 *
 * When true, the centering force is proportional to distance (linear pull-back).
 * THE key change in Bug #5361: at 30k nodes, normal-mode gravity (which falls
 * off as 1/distance) is too weak to hold the graph against repulsion at large
 * bounding-box widths. Live-browser test confirmed bounding box collapses from
 * 214,000 → 2,400 units when this is enabled, and movement drops from never-
 * converging to effectively settled in ~12 seconds.
 */
export const FA2_STRONG_GRAVITY_MODE = LAYOUT_DEFAULTS.strongGravityMode;

export interface ResolvedFA2Settings {
  scalingRatio: number;
  gravity: number;
  barnesHutOptimize: boolean;
  barnesHutTheta: number;
  slowDown: number;
  strongGravityMode: boolean;
}

/**
 * Pure function — derive the ForceAtlas2 supervisor settings from
 * the user-tunable {@link LayoutSettings} plus the runtime spread-mode and
 * clustering flags.
 *
 * Every knob is read from <c>layoutSettings</c> so a deployment can override
 * any of them through GUI_Settings without a rebuild (Bug #5361 made this
 * comprehensive; previously the multipliers + theta + slowDown + strongGravity
 * were hard-coded).
 *
 * When <c>isClustering</c> is set, the scalingRatio base switches from
 * <c>repulsion</c> to <c>clusterRepulsion</c> so the active-predicate members
 * spread apart enough to read their sub-structure.
 */
export function resolveFA2Settings(
  layoutSettings: LayoutSettings,
  isSpreadActive: boolean,
  isClustering = false,
): ResolvedFA2Settings {
  const baseGravity = layoutSettings.gravity * layoutSettings.gravityMultiplier;
  const gravity = isSpreadActive ? baseGravity * FA2_SPREAD_GRAVITY_FACTOR : baseGravity;
  const repulsionBase = isClustering ? layoutSettings.clusterRepulsion : layoutSettings.repulsion;
  return {
    scalingRatio: repulsionBase * layoutSettings.scalingRatioMultiplier,
    gravity,
    barnesHutOptimize: true,
    barnesHutTheta: layoutSettings.barnesHutTheta,
    slowDown: layoutSettings.slowDown,
    strongGravityMode: layoutSettings.strongGravityMode,
  };
}
