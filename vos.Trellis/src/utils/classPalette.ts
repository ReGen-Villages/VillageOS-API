// No fixed vocabulary of types: the caller picks which Thing-property identifies
// class membership (layoutSettings.classifyingProperty, default 'ifcClass') and
// the value is looked up in a registry of per-property bucket tables. Colors are
// curated for legibility on the dark graph background (#0c0a09).

export type ClassBucket =
  | 'spatial'    // top-level containers
  | 'structural' // load-bearing / shell elements
  | 'opening'    // openings and openings fills
  | 'mep'        // mechanical / electrical / plumbing
  | 'port'       // connection points — high-contrast, often the question subject
  | 'finish'     // surface finishes, furnishings
  | 'site'       // landscape, terrain
  | 'material'   // material graph nodes
  | 'typeDef'    // type-definition Things
  | 'other';     // catch-all

// Saturation chosen so adjacent buckets stay distinguishable when nodes overlap.
export const BUCKET_COLORS: Record<ClassBucket, string> = {
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

// Default table for IFC seeds; only classes vos.Tools.IfcIngest actually emits.
const IFC_BUCKET_TABLE: Record<string, ClassBucket> = {
  IfcSite: 'spatial',
  IfcBuilding: 'spatial',
  IfcBuildingStorey: 'spatial',
  IfcSpace: 'spatial',
  IfcProject: 'spatial',
  IfcZone: 'spatial',

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

  IfcDoor: 'opening',
  IfcWindow: 'opening',
  IfcOpeningElement: 'opening',

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

  // ports get a high-contrast color on purpose (often the question subject)
  IfcDistributionPort: 'port',
  IfcPort: 'port',

  IfcCovering: 'finish',
  IfcFurnishingElement: 'finish',
  IfcFurniture: 'finish',
  IfcSystemFurnitureElement: 'finish',

  IfcGeographicElement: 'site',
  IfcCivilElement: 'site',

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

// Suffix rule: IFC '*Type' classes all map to typeDef without listing each one.
type SuffixRule = {
  suffix: string;
  bucket: ClassBucket;
  prefix?: string;
};

const SUFFIX_RULES_BY_PROPERTY: Record<string, readonly SuffixRule[]> = {
  ifcClass: [{ suffix: 'Type', bucket: 'typeDef', prefix: 'Ifc' }],
};

const DEFAULT_BUCKET_TABLES: Record<string, Record<string, ClassBucket>> = {
  ifcClass: IFC_BUCKET_TABLE,
};

// Lookup order: default table, then suffix rule, then 'other' (also the fallback
// for null/empty values, so callers never branch on missing data).
export function getClassBucket(
  propertyName: string,
  value: string | null | undefined,
): ClassBucket {
  if (!value) return 'other';
  const table = DEFAULT_BUCKET_TABLES[propertyName];
  if (table && table[value]) return table[value];
  const rules = SUFFIX_RULES_BY_PROPERTY[propertyName];
  if (rules) {
    for (const r of rules) {
      if (value.endsWith(r.suffix) && (!r.prefix || value.startsWith(r.prefix))) return r.bucket;
    }
  }
  return 'other';
}

// Priority: per-value override (keyed by raw value), then bucket color.
// `overrides` is part of the API even though the GUI passes {} today.
export function resolveClassColor(
  propertyName: string,
  value: string | null | undefined,
  overrides: Record<string, string> = {},
): string {
  if (value && overrides[value]) return overrides[value];
  return BUCKET_COLORS[getClassBucket(propertyName, value)];
}
