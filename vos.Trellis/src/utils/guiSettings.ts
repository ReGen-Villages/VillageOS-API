import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import { DISPLAY_SETTINGS_ROLE_ID, thingBoundBy } from '@tendril-ui/tendril';
import { IS_PREDICATE } from '@tendril-ui/tendril';

export interface FlashSettings {
  flashEdgeSize: number;
  flashNodeSizeFactor: number;
  flashNodeBrighten: number;
}

/** Read from GUI_Settings, so a deployment can change any of these in the model or its seed without a rebuild. */
export interface LayoutSettings {
  // Repulsion for the whole graph; clusterRepulsion applies while a predicate cluster is active, so its
  // members spread apart enough to show their sub-structure.
  repulsion: number;
  gravity: number;
  clusterRepulsion: number;

  // Force-layout settings, checked against a very large building model.
  scalingRatioMultiplier: number;
  gravityMultiplier: number;
  barnesHutTheta: number;
  slowDown: number;
  strongGravityMode: boolean;

  nodeSizeMin: number;
  nodeSizeMax: number;
  nodeSizeSlope: number;

  // One width for every edge; a smaller one reduces clutter where many edges share endpoints.
  edgeSize: number;

  /** The property that says which class a Thing is in, for its colour in the graph and in BuildingDetail3D;
   *  the console knows no particular vocabulary, so a deployment names its own in
   *  `GUI_Settings.ClassifyingProperty`. A Thing without the property takes the default colour. */
  classifyingProperty: string;
}

export const LAYOUT_DEFAULTS: LayoutSettings = {
  repulsion: 0.1,
  gravity: 0.0001,
  clusterRepulsion: 0.4,
  scalingRatioMultiplier: 100,
  gravityMultiplier: 10000,
  // The ForceAtlas2 paper's value for very large graphs.
  barnesHutTheta: 1.2,
  slowDown: 10,
  // Gravity that weakens with distance cannot hold a very large graph against its repulsion.
  strongGravityMode: true,
  nodeSizeMin: 1,
  nodeSizeMax: 6,
  nodeSizeSlope: 0.4,
  edgeSize: 1,
  // A model imported from a building model renders by class without any setting.
  classifyingProperty: 'ifcClass',
};

/**
 * Read from the model, because only the model knows which count suits it: geometry, money and readings
 * each want a different one. Floating-point and decimal types are set apart, because one is a measurement
 * with limited significant digits and the other an exact quantity, as money is; whole numbers have none.
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

/**
 * An empty list sends everything, the right default for an untuned model and above all for an operations
 * model, whose few properties are live values a dashboard watches and should not wait for a second request.
 * A model that declares a list must name everything its pages read across the model, such as a dashboard's
 * bound properties and a pipeline's wiring; anything left out is fetched per page, not silently absent.
 */
export function readModelLoadProperties(properties: Record<string, unknown> | null): string[] {
  const raw = properties?.['ModelLoadProperties'];
  if (typeof raw !== 'string') return [];
  return raw.split(',').map((name) => name.trim()).filter((name) => name.length > 0);
}

/**
 * The display settings: the properties of the Thing the display settings role binds, under those of any
 * instance that `is` it. Null where the set holds no such binding.
 */
export function findGuiSettingsProperties(
  things: VosThing[],
  relationships: VosRelationship[],
): Record<string, unknown> | null {
  const settings = thingBoundBy(DISPLAY_SETTINGS_ROLE_ID, things, relationships);
  if (!settings) return null;

  const isPredicateId = things.find((t) => t.Name === IS_PREDICATE)?.Id;
  const isRelationship = relationships.find((r) => r.TargetId === settings.Id && r.PredicateId === isPredicateId);
  const instance = isRelationship
    ? things.find((t) => t.Id === isRelationship.SubjectId)
    : undefined;

  return {
    ...(settings.Properties || {}),
    ...((instance?.Properties) || {}),
  };
}

function readFlashSettings(p: Record<string, unknown>): FlashSettings {
  return {
    flashEdgeSize: toNumber(p['FlashEdgeSize'], FLASH_DEFAULTS.flashEdgeSize),
    flashNodeSizeFactor: toNumber(p['FlashNodeSizeFactor'], FLASH_DEFAULTS.flashNodeSizeFactor),
    flashNodeBrighten: toNumber(p['FlashNodeBrighten'], FLASH_DEFAULTS.flashNodeBrighten),
  };
}

function readLayoutSettings(p: Record<string, unknown>): LayoutSettings {
  return {
    repulsion: toNumber(p['LayoutRepulsion'], LAYOUT_DEFAULTS.repulsion),
    gravity: toNumber(p['LayoutGravity'], LAYOUT_DEFAULTS.gravity),
    clusterRepulsion: toNumber(p['ClusterRepulsion'], LAYOUT_DEFAULTS.clusterRepulsion),
    scalingRatioMultiplier: toNumber(p['LayoutScalingRatioMultiplier'], LAYOUT_DEFAULTS.scalingRatioMultiplier),
    gravityMultiplier: toNumber(p['LayoutGravityMultiplier'], LAYOUT_DEFAULTS.gravityMultiplier),
    barnesHutTheta: toNumber(p['LayoutBarnesHutTheta'], LAYOUT_DEFAULTS.barnesHutTheta),
    slowDown: toNumber(p['LayoutSlowDown'], LAYOUT_DEFAULTS.slowDown),
    strongGravityMode: toBoolean(p['LayoutStrongGravityMode'], LAYOUT_DEFAULTS.strongGravityMode),
    nodeSizeMin: toNumber(p['NodeSizeMin'], LAYOUT_DEFAULTS.nodeSizeMin),
    nodeSizeMax: toNumber(p['NodeSizeMax'], LAYOUT_DEFAULTS.nodeSizeMax),
    nodeSizeSlope: toNumber(p['NodeSizeSlope'], LAYOUT_DEFAULTS.nodeSizeSlope),
    edgeSize: toNumber(p['EdgeSize'], LAYOUT_DEFAULTS.edgeSize),
    classifyingProperty: toString(p['ClassifyingProperty'], LAYOUT_DEFAULTS.classifyingProperty),
  };
}

/** `PredicateColors` is a string holding a JSON object of predicate names to hex colours, e.g.
 *  `{"consumes":"#fb7185","produces":"#22d3ee"}`. */
function readPredicateColors(p: Record<string, unknown>): Record<string, string> {
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

export function extractAllGuiSettings(
  things: VosThing[],
  relationships: VosRelationship[],
): { flash: FlashSettings; layout: LayoutSettings; predicateColors: Record<string, string> } {
  const p = findGuiSettingsProperties(things, relationships);
  if (!p) return { flash: { ...FLASH_DEFAULTS }, layout: { ...LAYOUT_DEFAULTS }, predicateColors: {} };
  return { flash: readFlashSettings(p), layout: readLayoutSettings(p), predicateColors: readPredicateColors(p) };
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
