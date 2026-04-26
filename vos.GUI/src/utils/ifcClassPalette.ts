// Bug/Feature #5340 — bucketed color palette for IFC graph nodes.
//
// BuildingSMART defines ~100 IfcProduct subclasses; even a perfect 100-color
// categorical palette is unreadable. Ten semantic buckets keep the colors
// perceptually distinct and give the user actionable visual structure.
//
// All colors picked for legibility on the dark graph background (#0c0a09).
// Within a bucket, all members share a color — sub-class distinction is
// out of scope here (Feature #5341 will add a "color by ..." switch).

export type IfcClassBucket =
  | 'spatial'    // sites, buildings, storeys, spaces
  | 'structural' // walls, slabs, beams, columns, footings
  | 'opening'    // doors, windows, opening elements
  | 'mep'        // distribution, flow control, flow terminals
  | 'port'       // distribution ports — high-contrast, often the question subject
  | 'finish'     // coverings, furnishings
  | 'site'       // landscape elements
  | 'material'   // material graph (atomic, layered, composite)
  | 'typeDef'    // any *Type subclass
  | 'other';     // catch-all

/**
 * Bucket -> hex color. Curated for dark-bg legibility; saturation chosen so
 * adjacent buckets are distinguishable even with overlapping nodes (which
 * is common in the dense MV graph).
 */
export const BUCKET_COLORS: Record<IfcClassBucket, string> = {
  spatial:    '#d6c1a3', // warm stone
  structural: '#7a98b8', // steel-blue / slate
  opening:    '#fbbf24', // amber
  mep:        '#a78bfa', // violet
  port:       '#f0abfc', // magenta — high contrast
  finish:     '#c8a47e', // tan
  site:       '#86efac', // green-300
  material:   '#a8744d', // brown
  typeDef:    '#67e8f9', // cyan-300
  other:      '#94a3b8', // mid-gray slate-400
};

/**
 * Explicit IFC class -> bucket mapping. Only includes classes the IFC ingest
 * pipeline actually emits (verified against vos.Tools.IfcIngest extractors).
 * Anything not in this map and not matching the *Type suffix falls back to
 * 'other' so colors never silently shift when new classes appear.
 */
const EXPLICIT_BUCKETS: Record<string, IfcClassBucket> = {
  // spatial container
  IfcSite: 'spatial',
  IfcBuilding: 'spatial',
  IfcBuildingStorey: 'spatial',
  IfcSpace: 'spatial',
  IfcProject: 'spatial',
  IfcZone: 'spatial',

  // structural
  IfcWall: 'structural',
  IfcWallStandardCase: 'structural',
  IfcSlab: 'structural',
  IfcBeam: 'structural',
  IfcColumn: 'structural',
  IfcFooting: 'structural',
  IfcRoof: 'structural',
  IfcMember: 'structural',
  IfcPlate: 'structural',
  IfcStair: 'structural',
  IfcStairFlight: 'structural',
  IfcRamp: 'structural',
  IfcRampFlight: 'structural',
  IfcRailing: 'structural',
  IfcChimney: 'structural',
  IfcCurtainWall: 'structural',

  // openings
  IfcDoor: 'opening',
  IfcWindow: 'opening',
  IfcOpeningElement: 'opening',

  // MEP equipment / distribution
  IfcDistributionElement: 'mep',
  IfcDistributionFlowElement: 'mep',
  IfcDistributionControlElement: 'mep',
  IfcFlowController: 'mep',
  IfcFlowTerminal: 'mep',
  IfcFlowSegment: 'mep',
  IfcFlowFitting: 'mep',
  IfcFlowMovingDevice: 'mep',
  IfcFlowStorageDevice: 'mep',
  IfcFlowTreatmentDevice: 'mep',
  IfcEnergyConversionDevice: 'mep',

  // ports — bucket of one, but high-contrast on purpose
  IfcDistributionPort: 'port',
  IfcPort: 'port',

  // coverings & finishes
  IfcCovering: 'finish',
  IfcFurnishingElement: 'finish',
  IfcFurniture: 'finish',
  IfcSystemFurnitureElement: 'finish',

  // site / landscape
  IfcGeographicElement: 'site',
  IfcCivilElement: 'site',

  // material graph (atomic + layered + composite — Bug #5358)
  IfcMaterial: 'material',
  IfcMaterialLayer: 'material',
  IfcMaterialLayerSet: 'material',
  IfcMaterialLayerSetUsage: 'material',
  IfcMaterialConstituent: 'material',
  IfcMaterialConstituentSet: 'material',
  IfcMaterialProfile: 'material',
  IfcMaterialProfileSet: 'material',
  IfcMaterialProfileSetUsage: 'material',
};

/**
 * Resolve an IFC class name to its display bucket. Returns 'other' for
 * null / undefined / empty / unknown class names so the caller never has to
 * branch on missing data.
 *
 * The *Type suffix is handled by suffix match rather than enumeration —
 * IFC4 has dozens of *Type variants (IfcWallType, IfcDoorType, IfcSlabType,
 * IfcDistributionPortType, ...) and listing them is error-prone.
 */
export function getIfcClassBucket(ifcClass: string | null | undefined): IfcClassBucket {
  if (!ifcClass) return 'other';
  if (EXPLICIT_BUCKETS[ifcClass]) return EXPLICIT_BUCKETS[ifcClass];
  // *Type suffix — every IFC type-definition class ends in "Type".
  if (ifcClass.endsWith('Type') && ifcClass.startsWith('Ifc')) return 'typeDef';
  return 'other';
}

/**
 * Resolve a node color with the standard override priority chain:
 *
 *   1. Per-class override from the supplied map (e.g. user-customized via
 *      a future GUI_Settings panel — analogous to PredicateColors today)
 *   2. Curated bucket color (this file)
 *   3. 'other' bucket color (when ifcClass is missing or unknown)
 *
 * The `overrides` argument is intentionally part of the API even though the
 * GUI passes `{}` today; it documents the extension point and lets the
 * follow-up settings work add the runtime hook without changing call sites.
 */
export function resolveIfcClassColor(
  ifcClass: string | null | undefined,
  overrides: Record<string, string> = {},
): string {
  if (ifcClass && overrides[ifcClass]) return overrides[ifcClass];
  return BUCKET_COLORS[getIfcClassBucket(ifcClass)];
}
