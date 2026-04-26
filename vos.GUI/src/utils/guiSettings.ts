import type { VosThing, VosRelationship } from '../types/vos';

/** Flash-effect settings extracted from the GUI_Settings type Thing. */
export interface FlashSettings {
  flashEdgeSize: number;
  flashNodeSizeFactor: number;
  flashNodeBrighten: number;
}

/**
 * Force-layout settings extracted from the GUI_Settings type Thing.
 *
 * Bug #5361 added the second group (FA2 + node-size + edge-size knobs) so
 * the perf-critical parameters that used to be hard-coded constants are now
 * runtime-tunable through GUI_Settings, the same way attraction/repulsion/
 * gravity already were. Editing the GUI_Settings Thing in the broker (or in
 * the seed JSON) overrides any of these without a rebuild.
 */
export interface LayoutSettings {
  // Pre-existing user-tunable force coefficients
  attraction: number;
  repulsion: number;
  gravity: number;
  inertia: number;
  maxMove: number;
  clusterRepulsion: number;

  // FA2 supervisor knobs (Bug #5361) — all empirically validated against the
  // 30k-node MV graph; defaults are in LAYOUT_DEFAULTS below.
  scalingRatioMultiplier: number;
  gravityMultiplier: number;
  barnesHutTheta: number;
  slowDown: number;
  strongGravityMode: boolean;

  // Node sizing (Bug #5361) — incoming-degree → pixel size: clamp to
  // [nodeSizeMin, nodeSizeMax] of nodeSizeMin + degree * nodeSizeSlope.
  nodeSizeMin: number;
  nodeSizeMax: number;
  nodeSizeSlope: number;

  // Edge sizing (Bug #5361) — flat width for all edges. Smaller = less visual
  // clutter when many edges share endpoints.
  edgeSize: number;
}

export const LAYOUT_DEFAULTS: LayoutSettings = {
  attraction: 0.0005,
  repulsion: 0.1,
  gravity: 0.0001,
  inertia: 0.6,
  maxMove: 200,
  clusterRepulsion: 0.4,
  // FA2 — see fa2Settings.ts for the rationale on each value.
  scalingRatioMultiplier: 100,
  gravityMultiplier: 10000,
  barnesHutTheta: 1.2,
  slowDown: 10,
  strongGravityMode: true,
  // Node sizing — see nodeSize.ts.
  nodeSizeMin: 1,
  nodeSizeMax: 6,
  nodeSizeSlope: 0.4,
  // Edge sizing.
  edgeSize: 1,
};

export const FLASH_DEFAULTS: FlashSettings = {
  flashEdgeSize: 1.5,
  flashNodeSizeFactor: 1.4,
  flashNodeBrighten: 0.5,
};

const GUI_SETTINGS_TYPE_NAME = 'GUI_Settings';

/**
 * Find the GUI settings properties by locating the GUI_Settings type Thing
 * and any instance linked to it via an "is" relationship.
 * Instance own properties override type defaults.
 */
export function findGuiSettingsProperties(
  things: VosThing[],
  relationships: VosRelationship[],
): Record<string, unknown> | null {
  const typeThing = things.find((t) => t.Name === GUI_SETTINGS_TYPE_NAME);
  if (!typeThing) return null;

  // Find an instance linked via "is" to this type
  const isRel = relationships.find((r) => r.TargetId === typeThing.Id);
  const instance = isRel
    ? things.find((t) => t.Id === isRel.SubjectId)
    : undefined;

  // Merge: type defaults ← instance overrides
  return {
    ...(typeThing.Properties || {}),
    ...((instance?.Properties) || {}),
  };
}

/**
 * Extract flash settings from graph data.
 * Finds the GUI_Settings type Thing via relationships.
 * Returns defaults for any missing or invalid values.
 */
export function extractFlashSettings(
  things: VosThing[],
  relationships: VosRelationship[],
): FlashSettings {
  const p = findGuiSettingsProperties(things, relationships);
  if (!p) return { ...FLASH_DEFAULTS };

  return {
    flashEdgeSize: toNumber(p['FlashEdgeSize'], FLASH_DEFAULTS.flashEdgeSize),
    flashNodeSizeFactor: toNumber(p['FlashNodeSizeFactor'], FLASH_DEFAULTS.flashNodeSizeFactor),
    flashNodeBrighten: toNumber(p['FlashNodeBrighten'], FLASH_DEFAULTS.flashNodeBrighten),
  };
}

/**
 * Extract force-layout settings from graph data.
 * Finds the GUI_Settings type Thing via relationships.
 * Returns defaults for any missing or invalid values.
 */
export function extractLayoutSettings(
  things: VosThing[],
  relationships: VosRelationship[],
): LayoutSettings {
  const p = findGuiSettingsProperties(things, relationships);
  if (!p) return { ...LAYOUT_DEFAULTS };

  return readLayoutSettings(p);
}

/**
 * Internal — read every LayoutSettings field from a GUI_Settings property bag,
 * with defaults filling in any missing fields. Shared by both single-call and
 * combined-extraction code paths so the property names live in exactly one place.
 */
function readLayoutSettings(p: Record<string, unknown>): LayoutSettings {
  return {
    attraction: toNumber(p['LayoutAttraction'], LAYOUT_DEFAULTS.attraction),
    repulsion: toNumber(p['LayoutRepulsion'], LAYOUT_DEFAULTS.repulsion),
    gravity: toNumber(p['LayoutGravity'], LAYOUT_DEFAULTS.gravity),
    inertia: toNumber(p['LayoutInertia'], LAYOUT_DEFAULTS.inertia),
    maxMove: toNumber(p['LayoutMaxMove'], LAYOUT_DEFAULTS.maxMove),
    clusterRepulsion: toNumber(p['ClusterRepulsion'], LAYOUT_DEFAULTS.clusterRepulsion),
    // Bug #5361 — formerly hardcoded constants, now runtime-tunable
    scalingRatioMultiplier: toNumber(p['LayoutScalingRatioMultiplier'], LAYOUT_DEFAULTS.scalingRatioMultiplier),
    gravityMultiplier: toNumber(p['LayoutGravityMultiplier'], LAYOUT_DEFAULTS.gravityMultiplier),
    barnesHutTheta: toNumber(p['LayoutBarnesHutTheta'], LAYOUT_DEFAULTS.barnesHutTheta),
    slowDown: toNumber(p['LayoutSlowDown'], LAYOUT_DEFAULTS.slowDown),
    strongGravityMode: toBoolean(p['LayoutStrongGravityMode'], LAYOUT_DEFAULTS.strongGravityMode),
    nodeSizeMin: toNumber(p['NodeSizeMin'], LAYOUT_DEFAULTS.nodeSizeMin),
    nodeSizeMax: toNumber(p['NodeSizeMax'], LAYOUT_DEFAULTS.nodeSizeMax),
    nodeSizeSlope: toNumber(p['NodeSizeSlope'], LAYOUT_DEFAULTS.nodeSizeSlope),
    edgeSize: toNumber(p['EdgeSize'], LAYOUT_DEFAULTS.edgeSize),
  };
}

/**
 * Extract predicate→color overrides from graph data.
 * The GUI_Settings Thing may have a `PredicateColors` string property
 * containing a JSON object mapping predicate names to hex colours,
 * e.g. `{"consumes":"#fb7185","produces":"#22d3ee"}`.
 * Returns an empty object when the property is missing or invalid.
 */
export function extractPredicateColors(
  things: VosThing[],
  relationships: VosRelationship[],
): Record<string, string> {
  const p = findGuiSettingsProperties(things, relationships);
  if (!p) return {};

  const raw = p['PredicateColors'];
  if (typeof raw !== 'string' || !raw) return {};

  try {
    const parsed = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {};
    const result: Record<string, string> = {};
    for (const [key, value] of Object.entries(parsed)) {
      if (typeof value === 'string' && value.startsWith('#')) {
        result[key] = value;
      }
    }
    return result;
  } catch {
    return {};
  }
}

/**
 * Extract all GUI settings in a single traversal.
 * Calls `findGuiSettingsProperties` once and derives all setting groups.
 */
export function extractAllGuiSettings(
  things: VosThing[],
  relationships: VosRelationship[],
): { flash: FlashSettings; layout: LayoutSettings; predicateColors: Record<string, string> } {
  const p = findGuiSettingsProperties(things, relationships);

  const flash: FlashSettings = p
    ? {
        flashEdgeSize: toNumber(p['FlashEdgeSize'], FLASH_DEFAULTS.flashEdgeSize),
        flashNodeSizeFactor: toNumber(p['FlashNodeSizeFactor'], FLASH_DEFAULTS.flashNodeSizeFactor),
        flashNodeBrighten: toNumber(p['FlashNodeBrighten'], FLASH_DEFAULTS.flashNodeBrighten),
      }
    : { ...FLASH_DEFAULTS };

  const layout: LayoutSettings = p ? readLayoutSettings(p) : { ...LAYOUT_DEFAULTS };

  let predicateColors: Record<string, string> = {};
  if (p) {
    const raw = p['PredicateColors'];
    if (typeof raw === 'string' && raw) {
      try {
        const parsed = JSON.parse(raw);
        if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
          for (const [key, value] of Object.entries(parsed)) {
            if (typeof value === 'string' && value.startsWith('#')) {
              predicateColors[key] = value;
            }
          }
        }
      } catch {
        // invalid JSON — use empty overrides
      }
    }
  }

  return { flash, layout, predicateColors };
}

function toNumber(v: unknown, fallback: number): number {
  if (typeof v === 'number' && !isNaN(v)) return v;
  return fallback;
}

function toBoolean(v: unknown, fallback: boolean): boolean {
  if (typeof v === 'boolean') return v;
  return fallback;
}

