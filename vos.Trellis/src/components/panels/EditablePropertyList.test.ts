import { describe, it, expect } from 'vitest';
import { inferType, inferTypeFromText } from './EditablePropertyList';

describe('inferType', () => {
  it('returns "int" for integer numbers', () => {
    expect(inferType(0)).toBe('int');
    expect(inferType(1)).toBe('int');
    expect(inferType(-42)).toBe('int');
    expect(inferType(100)).toBe('int');
  });

  it('returns "double" for floating-point numbers', () => {
    expect(inferType(1.5)).toBe('double');
    expect(inferType(0.001)).toBe('double');
    expect(inferType(-3.14)).toBe('double');
    expect(inferType(99.99)).toBe('double');
  });

  it('returns "bool" for booleans', () => {
    expect(inferType(true)).toBe('bool');
    expect(inferType(false)).toBe('bool');
  });

  it('returns "string" for strings', () => {
    expect(inferType('')).toBe('string');
    expect(inferType('hello')).toBe('string');
    expect(inferType('123')).toBe('string');
  });

  it('returns "string" for null and undefined', () => {
    expect(inferType(null)).toBe('string');
    expect(inferType(undefined)).toBe('string');
  });

  it('returns "string" for objects and arrays', () => {
    expect(inferType({})).toBe('string');
    expect(inferType([])).toBe('string');
    expect(inferType({ key: 'value' })).toBe('string');
  });
});

describe('inferTypeFromText', () => {
  it('returns "double" for integer text', () => {
    expect(inferTypeFromText('0')).toBe('double');
    expect(inferTypeFromText('1')).toBe('double');
    expect(inferTypeFromText('-42')).toBe('double');
    expect(inferTypeFromText('100')).toBe('double');
  });

  it('returns "double" for floating-point text', () => {
    expect(inferTypeFromText('1.5')).toBe('double');
    expect(inferTypeFromText('0.001')).toBe('double');
    expect(inferTypeFromText('-3.14')).toBe('double');
  });

  it('returns "bool" for boolean text', () => {
    expect(inferTypeFromText('true')).toBe('bool');
    expect(inferTypeFromText('false')).toBe('bool');
  });

  it('returns "string" for non-numeric text', () => {
    expect(inferTypeFromText('hello')).toBe('string');
    expect(inferTypeFromText('')).toBe('string');
    expect(inferTypeFromText('abc123')).toBe('string');
  });
});
