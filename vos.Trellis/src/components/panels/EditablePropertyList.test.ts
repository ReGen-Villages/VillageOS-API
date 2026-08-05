import { describe, it, expect } from 'vitest';
import { inferTypeFromText } from './EditablePropertyList';
import { PROPERTY_TYPES, DEFAULT_PROPERTY_TYPE } from '../../utils/constants';

// Bug #6141 — the platform recognises only its own type names and answers anything else with
// "Invalid type specified", so a short name here fails every property write the panel makes.
const PLATFORM_TYPE = /^vos\.[A-Z]/;

describe('inferTypeFromText', () => {
  it('names a platform type for every kind of text', () => {
    expect(inferTypeFromText('42')).toMatch(PLATFORM_TYPE);
    expect(inferTypeFromText('true')).toMatch(PLATFORM_TYPE);
    expect(inferTypeFromText('hello')).toMatch(PLATFORM_TYPE);
  });

  it('reads numeric text as the widest numeric type', () => {
    expect(inferTypeFromText('0')).toBe('vos.Double');
    expect(inferTypeFromText('-42')).toBe('vos.Double');
    expect(inferTypeFromText('1.5')).toBe('vos.Double');
    expect(inferTypeFromText('-3.14')).toBe('vos.Double');
  });

  it('reads boolean text as a boolean', () => {
    expect(inferTypeFromText('true')).toBe('vos.Boolean');
    expect(inferTypeFromText('false')).toBe('vos.Boolean');
  });

  it('reads anything else as text', () => {
    expect(inferTypeFromText('hello')).toBe('vos.String');
    expect(inferTypeFromText('')).toBe('vos.String');
    expect(inferTypeFromText('abc123')).toBe('vos.String');
  });
});

describe('PROPERTY_TYPES', () => {
  it('offers only types the platform recognises', () => {
    for (const { value } of PROPERTY_TYPES) expect(value).toMatch(PLATFORM_TYPE);
  });

  it('starts the add row on one of the types it offers', () => {
    expect(PROPERTY_TYPES.map((t) => t.value)).toContain(DEFAULT_PROPERTY_TYPE);
  });
});
