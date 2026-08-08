import type { VosThing, VosRelationship } from '../types/vos';

/** Flash-effect settings extracted from the GUI_Settings type Thing. */
export interface FlashSettings {
  flashEdgeSize: number;
  flashNodeSizeFactor: number;
  flashNodeBrighten: number;
}

/**
 * ForceAtlas2 layout settings extracted from the GUI_Settings type Thing.
 *
 * Bug #5361 added the FA2 supervisor + node-size + edge-size knobs so the
 * perf-critical parameters that used to be hard-coded constants are now
 * runtime-tunable through GUI_Settings, the same way repulsion / gravity
 * already were. Feature #5362 added classifyingProperty so the GUI can run
 * against any domain ontology, not just IFC. Editing the GUI_Settings Thing
 * on Mycelium (or in the seed JSON) overrides any of these without a rebuild.
 */
export interface LayoutSettings {
  // FA2 scalingRatio bases: repulsion for the whole graph, clusterRepulsion
  // when a predicate cluster is active (members spread to show sub-structure).
  repulsion: number;
  gravity: number;
  clusterRepulsion: number;

  // FA2 supervisor knobs (Bug #5361) — empirically validated against the
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

  /**
   * Feature #5362 — name of the Thing-property whose value drives the per-node
   * color/bucket lookup and the per-element color in the BuildingDetail3D
   * viewport. The system itself knows nothing about specific ontologies; pick
   * whatever property the loaded model uses to identify class membership.
   *
   * Default `'ifcClass'` so IFC-imported seeds work out of the box. Any
   * non-IFC deployment overrides via `GUI_Settings.ClassifyingProperty`.
   * When the property is missing on a Thing, that Thing falls back to the
   * default bucket color.
   */
  classifyingProperty: string;
}

export const LAYOUT_DEFAULTS: LayoutSettings = {
  repulsion: 0.1,
  gravity: 0.0001,
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
  // Default to "ifcClass" so IFC-imported seeds work out of the box.
  classifyingProperty: 'ifcClass',
};

/**
 * How many decimal places a number shows, read from the GUI_Settings type Thing (#6163).
 *
 * There is no digit count that suits every model — a model of geometry, a model of money and a
 * model of sensor readings each want a different one, and the client cannot know which it is
 * looking at. The model that knows says so. The floating-point types and the decimal type are
 * separate settings because they exist for different reasons: one is a measurement with limited
 * significant digits, the other an exact quantity of the kind money is counted in. The whole-number
 * types take no setting, having no decimal places to show.
 */
export interface NumberDisplaySettings {
  floatingPointPrecision: number;
  decimalPrecision: number;
}

export const NUMBER_DISPLAY_DEFAULTS: NumberDisplaySettings = {
  floatingPointPrecision: 5,
  decimalPrecision: 5,
};

export const FLASH_DEFAULTS: FlashSettings = {
  flashEdgeSize: 1.5,
  flashNodeSizeFactor: 1.4,
  flashNodeBrighten: 0.5,
};

export const GUI_SETTINGS_TYPE_NAME = 'GUI_Settings';

/**
 * The properties a model says should travel with its model load (#6188), read from the settings Thing.
 *
 * An empty list means no narrowing — send everything. That is the right default for a model that has
 * not been tuned, and especially for an operations model, whose few properties are the live values a
 * dashboard is watching: deferring those would add a round trip to the only data anyone is looking at.
 *
 * A model that does declare a list has to name everything its pages read across the whole model, not
 * just what the graph draws with — a dashboard's bound properties, a pipeline's wiring. Anything left
 * out is fetched per surface, not silently absent.
 */
export function readModelLoadProperties(properties: Record<string, unknown> | null): string[] {
  const raw = properties?.['ModelLoadProperties'];
  if (typeof raw !== 'string') return [];
  return raw.split(',').map((name) => name.trim()).filter((name) => name.length > 0);
}

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
 * Extract ForceAtlas2 layout settings from graph data.
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
    repulsion: toNumber(p['LayoutRepulsion'], LAYOUT_DEFAULTS.repulsion),
    gravity: toNumber(p['LayoutGravity'], LAYOUT_DEFAULTS.gravity),
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
    // Feature #5362 — domain-agnostic classifier property name
    classifyingProperty: toString(p['ClassifyingProperty'], LAYOUT_DEFAULTS.classifyingProperty),
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

  const predicateColors: Record<string, string> = {};
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

export function extractNumberDisplaySettings(
  things: VosThing[],
  relationships: VosRelationship[],
): NumberDisplaySettings {
  const p = findGuiSettingsProperties(things, relationships);
  if (!p) return { ...NUMBER_DISPLAY_DEFAULTS };

  return {
    floatingPointPrecision: toDecimalPlaces(p['FloatingPointDisplayPrecision'], NUMBER_DISPLAY_DEFAULTS.floatingPointPrecision),
    decimalPrecision: toDecimalPlaces(p['DecimalDisplayPrecision'], NUMBER_DISPLAY_DEFAULTS.decimalPrecision),
  };
}

/** A count of decimal places has to be a whole number no smaller than zero, and toFixed rejects
 *  anything past twenty. A setting outside that says nothing usable, so the default stands. */
function toDecimalPlaces(v: unknown, fallback: number): number {
  if (typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 20) return v;
  return fallback;
}

function toNumber(v: unknown, fallback: number): number {
  if (typeof v === 'number' && !isNaN(v)) return v;
  return fallback;
}

function toBoolean(v: unknown, fallback: boolean): boolean {
  if (typeof v === 'boolean') return v;
  return fallback;
}

function toString(v: unknown, fallback: string): string {
  if (typeof v === 'string' && v.length > 0) return v;
  return fallback;
}
