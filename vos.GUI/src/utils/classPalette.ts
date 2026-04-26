// Bug/Feature #5340 — bucketed color palette for graph nodes.
//
// The system has no fixed vocabulary of types. The caller decides which
// Thing-property identifies class membership (`layoutSettings.classifyingProperty`,
// default 'ifcClass') and the resolver looks the value up in a registry of
// per-property bucket tables. The IFC default table ships under the
// 'ifcClass' key for IFC seed convenience; non-IFC deployments get the
// 'other' bucket color (or hash-driven palette via overrides) until they
// register their own table or supply per-value overrides.
//
// All curated colors are picked for legibility on the dark graph background
// (#0c0a09). Within a bucket, all members share a color — sub-class
// distinction is out of scope here (Feature #5341 will add a "color by ..."
// switch).

/**
 * Generic semantic buckets. Members of one bucket share a color so the user
 * can tell at a glance which group a node belongs to. The bucket NAMES are
 * domain-agnostic; whether 'spatial' contains IfcSite or some other-domain
 * "Region" Thing depends entirely on what's in the per-property table.
 */
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

/**
 * Bucket -> hex color. Curated for dark-bg legibility; saturation chosen so
 * adjacent buckets are distinguishable even with overlapping nodes.
 */
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

/**
 * Convenience default table for IFC-imported seeds. Only includes classes the
 * IFC ingest pipeline actually emits (verified against vos.Tools.IfcIngest
 * extractors). This table is registered against the property name 'ifcClass'
 * in DEFAULT_BUCKET_TABLES below — non-IFC deployments simply set
 * `classifyingProperty` to a different property name (or supply user
 * overrides via GUI_Settings) and this table won't apply.
 *
 * Lives inline rather than in a separate `ifcDefaults.ts` to keep the
 * registry visible alongside the resolver code; the user sees one mapping
 * at the same scroll position as the lookup that consumes it.
 */
const IFC_BUCKET_TABLE: Record<string, ClassBucket> = {
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
 * Optional per-property suffix rule. For IFC, classes ending in 'Type'
 * (IfcWallType, IfcDoorType, ...) all belong to the typeDef bucket — listing
 * each is error-prone. Other property names get no implicit suffix rule;
 * deployments that want one register it via the same registry pattern.
 */
type SuffixRule = {
  suffix: string;
  bucket: ClassBucket;
  /** Additional gate, e.g. require the value to start with 'Ifc'. Optional. */
  prefix?: string;
};

const SUFFIX_RULES_BY_PROPERTY: Record<string, readonly SuffixRule[]> = {
  ifcClass: [{ suffix: 'Type', bucket: 'typeDef', prefix: 'Ifc' }],
};

/**
 * Registry of default bucket tables, keyed by the property name the value
 * was read from. Adding a new domain default = registering a new entry here
 * (or supplying it at runtime via GUI_Settings overrides — same shape).
 */
const DEFAULT_BUCKET_TABLES: Record<string, Record<string, ClassBucket>> = {
  ifcClass: IFC_BUCKET_TABLE,
};

/**
 * Resolve a property/value pair to its display bucket. Pure function.
 *
 * Lookup order:
 *   1. Registered default table for the given property name
 *   2. Suffix rule registered for that property name
 *   3. 'other' bucket
 *
 * Returns 'other' for null / undefined / empty / non-string values so the
 * caller never has to branch on missing data.
 */
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

/**
 * Resolve a node color with the standard override priority chain:
 *
 *   1. Per-value override from the supplied map (e.g. user-customized via
 *      a future GUI_Settings panel — analogous to PredicateColors today).
 *      The override map is keyed by raw value (no propertyName prefix); the
 *      caller is responsible for scoping overrides to the right property.
 *   2. Curated bucket color from the registered default table for this
 *      property name.
 *   3. 'other' bucket color (missing / unknown value).
 *
 * The `overrides` argument is intentionally part of the API even though the
 * GUI passes `{}` today; it documents the extension point and lets the
 * follow-up settings work add the runtime hook without changing call sites.
 */
export function resolveClassColor(
  propertyName: string,
  value: string | null | undefined,
  overrides: Record<string, string> = {},
): string {
  if (value && overrides[value]) return overrides[value];
  return BUCKET_COLORS[getClassBucket(propertyName, value)];
}
