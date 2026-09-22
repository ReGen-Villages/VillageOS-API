// The platform's own type names (vos.Core VosType), which its write routes look every incoming name
// up in. It accepts no others — a short name like "double" is answered with "Invalid type specified".
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
] as const;

export type VosTypeName = (typeof VOS_TYPES)[number];

/**
 * A reported type name narrowed to one the write routes accept, or null when it is not one.
 *
 * A name the client writes is checked when the code is compiled. A name a property reports is not:
 * it arrives over the network, where a compiler cannot reach. Declaring it as one of the set would
 * be an assertion nobody verified. A property's type is a plain settable string on the platform, so
 * one can hold a name outside the set — readable, but rejected by every write route. Narrowing here
 * turns that into a refusal that names the type, instead of an opaque "Invalid type specified".
 */
export function asVosTypeName(name: string): VosTypeName | null {
  return (VOS_TYPES as readonly string[]).includes(name) ? (name as VosTypeName) : null;
}

// What the add-property row offers, a deliberate subset: the complex types (IfcGeometry, GeoJson)
// are written by ingest, not typed into a text box.
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
