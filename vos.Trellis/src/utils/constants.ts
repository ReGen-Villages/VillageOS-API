// The platform's own type names (vos.Core VosType); its write routes refuse any other, such as "double".
export const VOS_TYPES = [
  'vos.String',
  'vos.Integer',
  'vos.LongInteger',
  'vos.Double',
  'vos.Float',
  'vos.Boolean',
  'vos.Decimal',
  'vos.DateTime',
  'vos.Guid',
  'vos.IfcGeometry',
  'vos.GeoJson',
  'vos.Grid',
] as const;

export type VosTypeName = (typeof VOS_TYPES)[number];

/** A reported name arrives over the network, where the compiler cannot check it, and a property's type is a
 *  plain string that can hold a name every write route refuses; narrowing here makes that a refusal naming
 *  the type rather than "Invalid type specified". */
export function asVosTypeName(name: string): VosTypeName | null {
  return (VOS_TYPES as readonly string[]).includes(name) ? (name as VosTypeName) : null;
}

// What the add-property row offers, a deliberate subset: the complex types (IfcGeometry, GeoJson,
// Grid) are written by ingest and services, not typed into a text box.
export const PROPERTY_TYPES: { label: string; value: VosTypeName }[] = [
  { label: 'string', value: 'vos.String' },
  { label: 'int', value: 'vos.Integer' },
  { label: 'long', value: 'vos.LongInteger' },
  { label: 'double', value: 'vos.Double' },
  { label: 'float', value: 'vos.Float' },
  { label: 'bool', value: 'vos.Boolean' },
  { label: 'decimal', value: 'vos.Decimal' },
  { label: 'DateTime', value: 'vos.DateTime' },
  { label: 'Guid', value: 'vos.Guid' },
];

/** What the add-property row starts on — it has to be one of the offered values or the dropdown
 *  shows a blank selection and submits a type nothing chose. */
export const DEFAULT_PROPERTY_TYPE: VosTypeName = 'vos.String';
