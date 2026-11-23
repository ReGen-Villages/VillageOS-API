// The caller names the property that says which class a Thing is in (layoutSettings.classifyingProperty);
// the colours are chosen to read on the dark graph background.

export type ClassBucket =
  | 'spatial'    // top-level containers
  | 'structural' // load-bearing and shell elements
  | 'opening'    // openings and what fills them
  | 'mep'        // mechanical, electrical and plumbing
  | 'port'       // connection points, in high contrast because a port is often what a question is about
  | 'finish'     // surface finishes and furnishings
  | 'site'       // landscape and terrain
  | 'material'
  | 'typeDef'
  | 'other';

// Saturation chosen so adjacent buckets stay distinguishable when nodes overlap.
export const BUCKET_COLORS: Record<ClassBucket, string> = {
  spatial:    '#d6c1a3',
  structural: '#7a98b8',
  opening:    '#fbbf24',
  mep:        '#a78bfa',
  port:       '#f0abfc',
  finish:     '#c8a47e',
  site:       '#86efac',
  material:   '#a8744d',
  typeDef:    '#67e8f9',
  other:      '#94a3b8',
};

// Default table for IFC seeds; only classes vos.Tools.ModelIngest actually emits.
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

export function resolveClassColor(propertyName: string, value: string | null | undefined): string {
  return BUCKET_COLORS[getClassBucket(propertyName, value)];
}
