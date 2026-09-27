import type { LayoutSettings } from './guiSettings';

/** Spread mode reduces gravity by this factor so the graph has room to open up. */
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
 * Derive the ForceAtlas2 supervisor settings from the user-tunable
 * {@link LayoutSettings} plus the runtime spread-mode and clustering flags.
 *
 * Every knob is read from <c>layoutSettings</c> so a deployment can override
 * any of them through GUI_Settings without a rebuild.
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
