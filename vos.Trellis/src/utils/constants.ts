// The platform's own type names (vos.Core VosType). It accepts no others — a short name like
// "double" is answered with "Invalid type specified". The complex types (IfcGeometry, GeoJson)
// are left out: they are written by ingest, not typed into a text box.
export const PROPERTY_TYPES = [
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
export const DEFAULT_PROPERTY_TYPE = 'vos.String';
