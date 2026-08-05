import { describe, it, expect } from 'vitest';
import { formatGuid, formatTimestamp, formatDateTime, formatRelativeTime, formatMs, formatPropertyValue } from './formatters';

describe('formatGuid', () => {
  it('truncates a GUID to first 8 chars with ellipsis', () => {
    expect(formatGuid('abcdef01-2345-6789-abcd-ef0123456789')).toBe('abcdef01...');
  });

  it('handles short strings', () => {
    expect(formatGuid('abc')).toBe('abc...');
  });
});

describe('formatTimestamp', () => {
  it('formats an ISO string to HH:mm:ss', () => {
    const result = formatTimestamp('2025-06-15T14:30:45.000Z');
    // The exact output depends on timezone, but it should be a time string
    expect(result).toMatch(/^\d{2}:\d{2}:\d{2}$/);
  });

  it('returns the original string on invalid input', () => {
    expect(formatTimestamp('not-a-date')).toBe('not-a-date');
  });
});

describe('formatDateTime', () => {
  it('formats an ISO string to yyyy-MM-dd HH:mm:ss', () => {
    const result = formatDateTime('2025-06-15T14:30:45.000Z');
    expect(result).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/);
  });

  it('returns the original string on invalid input', () => {
    expect(formatDateTime('bad')).toBe('bad');
  });
});

describe('formatRelativeTime', () => {
  it('returns a relative time string with suffix', () => {
    // 1 hour ago
    const oneHourAgo = new Date(Date.now() - 3600 * 1000).toISOString();
    const result = formatRelativeTime(oneHourAgo);
    expect(result).toMatch(/ago$/);
  });

  it('returns the original string on invalid input', () => {
    expect(formatRelativeTime('not-a-date')).toBe('not-a-date');
  });
});

describe('formatMs', () => {
  it('returns <1ms for sub-millisecond values', () => {
    expect(formatMs(0.5)).toBe('<1ms');
  });

  it('rounds milliseconds', () => {
    expect(formatMs(42)).toBe('42ms');
    expect(formatMs(999)).toBe('999ms');
  });

  it('converts to seconds above 1000ms', () => {
    expect(formatMs(1500)).toBe('1.5s');
    expect(formatMs(10000)).toBe('10.0s');
  });
});

describe('formatPropertyValue', () => {
  it('returns (null) for null', () => {
    expect(formatPropertyValue(null)).toBe('(null)');
  });

  it('returns (null) for undefined', () => {
    expect(formatPropertyValue(undefined)).toBe('(null)');
  });

  it('stringifies objects', () => {
    expect(formatPropertyValue({ a: 1 })).toBe('{"a":1}');
  });

  it('converts primitives to string', () => {
    expect(formatPropertyValue(42)).toBe('42');
    expect(formatPropertyValue(true)).toBe('true');
    expect(formatPropertyValue('hello')).toBe('hello');
  });
});

// The value alone cannot say how it should read: a date arrives as the machine timestamp the
// platform stores, an identifier at full length, geometry as a JSON body. The platform declares a
// type for every property, and that is the answer this was guessing at (#6163).
describe('formatPropertyValue by declared type', () => {
  const numbers = { floatingPointPrecision: 5, decimalPrecision: 2 };

  it('reads a date as a date, not as the stored timestamp', () => {
    expect(formatPropertyValue('2026-08-05T09:20:14Z', 'vos.DateTime')).toMatch(/^2026-08-05 \d{2}:20:14$/);
  });

  it('shortens an identifier instead of filling the cell', () => {
    expect(formatPropertyValue('905abcab-913a-5941-a3e8-a24570de383a', 'vos.Guid')).toBe('905abcab...');
  });

  it('holds a floating-point reading to the places the model asks for', () => {
    expect(formatPropertyValue(0.123456789, 'vos.Double', numbers)).toBe('0.12346');
    expect(formatPropertyValue(0.123456789, 'vos.Float', numbers)).toBe('0.12346');
  });

  it('holds a decimal amount to its own separate setting', () => {
    expect(formatPropertyValue(12.3456, 'vos.Decimal', numbers)).toBe('12.35');
  });

  it('treats the places as a maximum, so a short value shows no padding', () => {
    expect(formatPropertyValue(0.5, 'vos.Double', numbers)).toBe('0.5');
    expect(formatPropertyValue(100, 'vos.Double', numbers)).toBe('100');
  });

  it('leaves the whole-number types alone, having no places to show', () => {
    expect(formatPropertyValue(4711, 'vos.Integer', numbers)).toBe('4711');
    expect(formatPropertyValue(90071992547409, 'vos.LongInteger', numbers)).toBe('90071992547409');
  });

  it('summarises a mesh instead of emptying it into the cell', () => {
    const mesh = { positions: new Array(300).fill(0), indices: new Array(150).fill(0), normals: [] };
    expect(formatPropertyValue(mesh, 'vos.IfcGeometry')).toBe('mesh (100 vertices, 50 triangles)');
  });

  it('names a shape by its own type word', () => {
    const shape = { Json: JSON.stringify({ type: 'Polygon', coordinates: [[[0, 0]]] }) };
    expect(formatPropertyValue(shape, 'vos.GeoJson')).toBe('Polygon');
  });

  it('keeps text as text, however numeric it looks', () => {
    expect(formatPropertyValue('4711', 'vos.String', numbers)).toBe('4711');
  });

  // A badly formatted value is recoverable; a missing one is not.
  it('falls back to the shape of the value when the type is unknown or absent', () => {
    expect(formatPropertyValue(0.123456789, 'acme.Reading', numbers)).toBe('0.123456789');
    expect(formatPropertyValue({ a: 1 }, 'acme.Blob')).toBe('{"a":1}');
    expect(formatPropertyValue(0.123456789)).toBe('0.123456789');
  });

  it('falls back rather than mangling a value that is not what its type says', () => {
    expect(formatPropertyValue('not a date', 'vos.DateTime')).toBe('not a date');
    expect(formatPropertyValue('not a number', 'vos.Double', numbers)).toBe('not a number');
    expect(formatPropertyValue({ shapeless: true }, 'vos.GeoJson')).toBe('GeoJson');
    expect(formatPropertyValue({ shapeless: true }, 'vos.IfcGeometry')).toBe('mesh');
  });

  it('names a shape it cannot read, rather than throwing on it', () => {
    expect(formatPropertyValue({ Json: 'not json at all' }, 'vos.GeoJson')).toBe('GeoJson');
    expect(formatPropertyValue({ Json: '{"coordinates":[]}' }, 'vos.GeoJson')).toBe('GeoJson');
  });

  it('takes a type only from the value, never from the type name alone', () => {
    expect(formatPropertyValue(20260805, 'vos.DateTime')).toBe('20260805');
    expect(formatPropertyValue(42, 'vos.Guid')).toBe('42');
  });

  it('shows a mesh with no triangles as the vertices it has', () => {
    expect(formatPropertyValue({ positions: new Array(9).fill(0) }, 'vos.IfcGeometry')).toBe('mesh (3 vertices)');
  });

  it('rounds to whole numbers when the model asks for no places at all', () => {
    const noPlaces = { floatingPointPrecision: 0, decimalPrecision: 0 };
    expect(formatPropertyValue(12.7, 'vos.Double', noPlaces)).toBe('13');
    expect(formatPropertyValue(12.2, 'vos.Decimal', noPlaces)).toBe('12');
  });

  it('leaves a number it cannot hold to the places setting alone', () => {
    expect(formatPropertyValue(Infinity, 'vos.Double', numbers)).toBe('Infinity');
    expect(formatPropertyValue(NaN, 'vos.Decimal', numbers)).toBe('NaN');
  });

  it('says (null) whatever the type', () => {
    expect(formatPropertyValue(null, 'vos.Double', numbers)).toBe('(null)');
  });

  it('defaults to five places when the model states none', () => {
    expect(formatPropertyValue(0.123456789, 'vos.Double')).toBe('0.12346');
    expect(formatPropertyValue(0.123456789, 'vos.Decimal')).toBe('0.12346');
  });
});
