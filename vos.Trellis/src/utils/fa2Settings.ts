import type { LayoutSettings } from './guiSettings';

/** Spread mode reduces gravity by this factor so the graph has room to open up. */
export const FA2_SPREAD_GRAVITY_FACTOR = 0.1;

interface ResolvedFA2Settings {
  scalingRatio: number;
  gravity: number;
  barnesHutOptimize: boolean;
  barnesHutTheta: number;
  slowDown: number;
  strongGravityMode: boolean;
}

/** Every setting is read from `layoutSettings`, so a deployment can change any of them in GUI_Settings without
 *  a rebuild. While clustering, the scaling starts from `clusterRepulsion`, so the active members spread far
 *  enough apart to read their structure. */
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
