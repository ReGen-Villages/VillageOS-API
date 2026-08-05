import { describe, it, expect } from 'vitest';
import { editorForType, rejectionKeyForType } from './propertyEditing';
import { VOS_TYPES } from '../../utils/constants';

describe('editorForType', () => {
  it('offers a checkbox for true/false, not a box to type the words into', () => {
    expect(editorForType('vos.Boolean')).toBe('checkbox');
  });

  it('offers a picker for a date, not a box to type a timestamp into', () => {
    expect(editorForType('vos.DateTime')).toBe('dateTime');
  });

  it('tells the whole-number types apart from the ones that take a fraction', () => {
    expect(editorForType('vos.Integer')).toBe('wholeNumber');
    expect(editorForType('vos.LongInteger')).toBe('wholeNumber');
    expect(editorForType('vos.Double')).toBe('number');
    expect(editorForType('vos.Float')).toBe('number');
    expect(editorForType('vos.Decimal')).toBe('number');
  });

  it('offers no inline control for the types written by ingest', () => {
    expect(editorForType('vos.IfcGeometry')).toBe('readOnly');
    expect(editorForType('vos.GeoJson')).toBe('readOnly');
  });

  it('falls back to a text box for text and for a type it does not know', () => {
    expect(editorForType('vos.String')).toBe('text');
    expect(editorForType('vos.Guid')).toBe('text');
    expect(editorForType('acme.PartNumber')).toBe('text');
  });

  it('has an answer for every type the platform accepts', () => {
    for (const type of VOS_TYPES) expect(editorForType(type)).toBeTruthy();
  });
});

describe('rejectionKeyForType', () => {
  it('refuses a fraction in a whole-number property', () => {
    // The platform would convert it and drop the fractional part — a quiet wrong answer.
    expect(rejectionKeyForType('3.7', 'vos.Integer')).toBe('panels.props.rejected.wholeNumber');
    expect(rejectionKeyForType('3', 'vos.Integer')).toBeNull();
    expect(rejectionKeyForType('-3', 'vos.LongInteger')).toBeNull();
  });

  it('refuses letters in a numeric property', () => {
    expect(rejectionKeyForType('twelve', 'vos.Double')).toBe('panels.props.rejected.number');
    expect(rejectionKeyForType('12.5', 'vos.Double')).toBeNull();
    expect(rejectionKeyForType('-0.25', 'vos.Decimal')).toBeNull();
  });

  it('refuses text that is not a date', () => {
    expect(rejectionKeyForType('next tuesday', 'vos.DateTime')).toBe('panels.props.rejected.dateTime');
    expect(rejectionKeyForType('2026-08-05T09:20:14Z', 'vos.DateTime')).toBeNull();
  });

  it('refuses text that is not an identifier', () => {
    expect(rejectionKeyForType('nope', 'vos.Guid')).toBe('panels.props.rejected.identifier');
    expect(rejectionKeyForType('905abcab-913a-5941-a3e8-a24570de383a', 'vos.Guid')).toBeNull();
  });

  it('refuses anything for a type with no inline control', () => {
    expect(rejectionKeyForType('{}', 'vos.GeoJson')).toBe('panels.props.rejected.readOnly');
  });

  // Clearing a property is what the delete button is for. An empty numeric field is a half-finished
  // edit, not an instruction to store nothing.
  it('refuses empty text for every type but text', () => {
    expect(rejectionKeyForType('', 'vos.Integer')).toBe('panels.props.rejected.wholeNumber');
    expect(rejectionKeyForType('   ', 'vos.Double')).toBe('panels.props.rejected.number');
    expect(rejectionKeyForType('', 'vos.DateTime')).toBe('panels.props.rejected.dateTime');
    expect(rejectionKeyForType('', 'vos.String')).toBeNull();
  });

  it('accepts anything a text property can hold, however numeric it looks', () => {
    expect(rejectionKeyForType('4711', 'vos.String')).toBeNull();
    expect(rejectionKeyForType('', 'acme.PartNumber')).toBeNull();
  });

  it('takes only the two words for true/false', () => {
    expect(rejectionKeyForType('true', 'vos.Boolean')).toBeNull();
    expect(rejectionKeyForType('false', 'vos.Boolean')).toBeNull();
    expect(rejectionKeyForType('yes', 'vos.Boolean')).toBe('panels.props.rejected.boolean');
  });
});
