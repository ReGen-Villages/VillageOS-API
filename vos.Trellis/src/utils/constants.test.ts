import { describe, it, expect } from 'vitest';
import { VOS_TYPES, PROPERTY_TYPES, DEFAULT_PROPERTY_TYPE, asVosTypeName, type VosTypeName } from './constants';

// Bug (#6141): the panel sent the short name "double" where the platform wanted "vos.Double".
// Nothing checked it, so every property write from the GUI failed for months. These type names are
// now a named set, which puts that mistake in front of the compiler.
describe('a type name the client writes is checked when the code is compiled', () => {
  it('rejects the retired short name that Bug #6141 sent', () => {
    // @ts-expect-error - "double" is the retired short name; the platform answers it with "Invalid
    // type specified". If this line stops erroring, the set has stopped checking anything.
    const retired: VosTypeName = 'double';
    expect(asVosTypeName(retired)).toBeNull();
  });

  it('accepts the platform name it should have sent', () => {
    const accepted: VosTypeName = 'vos.Double';
    expect(asVosTypeName(accepted)).toBe('vos.Double');
  });
});

// A reported type arrives over the network, where the compiler cannot reach. A property's type is a
// plain settable string on the platform, so one can report a name no write route accepts.
describe('a reported name is narrowed before it is used', () => {
  it('gives back a name the write routes accept', () => {
    expect(asVosTypeName('vos.GeoJson')).toBe('vos.GeoJson');
  });

  it('gives back nothing for a name outside the set, so it is never sent', () => {
    expect(asVosTypeName('acme.PartNumber')).toBeNull();
    expect(asVosTypeName('')).toBeNull();
  });

  it('does not treat a name that merely starts the same as one of the set', () => {
    expect(asVosTypeName('vos.Str')).toBeNull();
    expect(asVosTypeName('vos.Stringy')).toBeNull();
  });
});

describe('the add-property dropdown', () => {
  // Deliberately a subset: the complex types are written by ingest, not typed into a text box. What
  // it must never be is a superset, which would offer a type every write route then rejects.
  it('offers only names the platform accepts', () => {
    const offered = PROPERTY_TYPES.map((option) => option.value);
    expect(offered.filter((name) => !asVosTypeName(name))).toEqual([]);
  });

  it('leaves out the types that are written by ingest rather than typed', () => {
    const offered = PROPERTY_TYPES.map((option) => option.value);
    expect(offered).not.toContain('vos.IfcGeometry');
    expect(offered).not.toContain('vos.GeoJson');
  });

  it('starts on a type it offers, so nothing submits a selection the dropdown never showed', () => {
    expect(PROPERTY_TYPES.map((option) => option.value)).toContain(DEFAULT_PROPERTY_TYPE);
  });

  it('gives every offered type its own name', () => {
    const offered = PROPERTY_TYPES.map((option) => option.value);
    expect(new Set(offered).size).toBe(offered.length);
  });
});

describe('the set itself', () => {
  it('names each type once', () => {
    expect(new Set(VOS_TYPES).size).toBe(VOS_TYPES.length);
  });

  it('carries the prefix the write routes look up, on every name', () => {
    expect(VOS_TYPES.filter((name) => !name.startsWith('vos.'))).toEqual([]);
  });
});
