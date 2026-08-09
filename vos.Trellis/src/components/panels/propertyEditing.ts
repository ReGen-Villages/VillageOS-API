/**
 * What a property's declared type means for editing it: which control to offer, and whether the
 * text in that control is something the type can actually hold (#6164).
 *
 * The platform converts an incoming value to the property's stored type and rejects what it cannot
 * convert, and that stays the authority. Checking here is not to make that check redundant — it is
 * to catch the mistake at the keystroke, where the user can still see what they typed, rather than
 * after a round trip as a message detached from the field that caused it.
 */

export type EditorKind = 'checkbox' | 'dateTime' | 'wholeNumber' | 'number' | 'text' | 'readOnly';

/** Types with no inline control worth offering. A JSON body typed into a narrow panel field is not
 *  editing, and a half-valid body saved into one of these is worse than no edit at all. */
const READ_ONLY_TYPES = new Set(['vos.IfcGeometry', 'vos.GeoJson']);

export function editorForType(type: string): EditorKind {
  if (READ_ONLY_TYPES.has(type)) return 'readOnly';
  switch (type) {
    case 'vos.Boolean':
      return 'checkbox';
    case 'vos.DateTime':
      return 'dateTime';
    case 'vos.Integer':
    case 'vos.LongInteger':
      return 'wholeNumber';
    case 'vos.Double':
    case 'vos.Float':
    case 'vos.Decimal':
      return 'number';
    default:
      return 'text';
  }
}

/** Named as literals so a caller can pass one straight to the translator, which only accepts keys
 *  the English base defines. */
export type RejectionKey =
  | 'panels.props.rejected.wholeNumber'
  | 'panels.props.rejected.number'
  | 'panels.props.rejected.dateTime'
  | 'panels.props.rejected.identifier'
  | 'panels.props.rejected.boolean'
  | 'panels.props.rejected.readOnly';

const WHOLE_NUMBER = /^-?\d+$/;
const IDENTIFIER = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/**
 * The translation key naming why this text cannot be saved as this type, or null when it can.
 *
 * Empty text is refused for every type that is not text. Clearing a property is what the delete
 * button is for; an empty numeric field is a half-finished edit, not an instruction.
 */
export function rejectionKeyForType(text: string, type: string): RejectionKey | null {
  const trimmed = text.trim();

  switch (editorForType(type)) {
    case 'wholeNumber':
      // Rejecting a fraction is not tidiness: the platform would convert it and drop the fractional
      // part, which is exactly the quiet wrong answer this work exists to remove.
      return WHOLE_NUMBER.test(trimmed) && Number.isFinite(Number(trimmed))
        ? null
        : 'panels.props.rejected.wholeNumber';
    case 'number':
      return trimmed !== '' && Number.isFinite(Number(trimmed)) ? null : 'panels.props.rejected.number';
    case 'dateTime':
      return trimmed !== '' && !Number.isNaN(Date.parse(trimmed)) ? null : 'panels.props.rejected.dateTime';
    case 'checkbox':
      return trimmed === 'true' || trimmed === 'false' ? null : 'panels.props.rejected.boolean';
    case 'readOnly':
      return 'panels.props.rejected.readOnly';
    default:
      return type === 'vos.Guid' && !IDENTIFIER.test(trimmed) ? 'panels.props.rejected.identifier' : null;
  }
}
