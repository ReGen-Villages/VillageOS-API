import { describe, it, expect } from 'vitest';
import { parseParameterValue } from './parseParamValue';

// The Params bar parses values as JSON when valid so lists drive fan-out and numbers stay numbers.
describe('parseParamValue', () => {
  it('parses JSON lists, numbers, booleans, and objects', () => {
    expect(parseParameterValue('["a","b","c"]')).toEqual(['a', 'b', 'c']);
    expect(parseParameterValue('42')).toBe(42);
    expect(parseParameterValue('true')).toBe(true);
    expect(parseParameterValue('{"k":1}')).toEqual({ k: 1 });
  });

  it('keeps non-JSON text as a plain string', () => {
    expect(parseParameterValue('hello')).toBe('hello');
    expect(parseParameterValue('  spaced  ')).toBe('  spaced  '); // raw (not the trimmed form) when not JSON
  });

  it('treats empty input as an empty string', () => {
    expect(parseParameterValue('')).toBe('');
    expect(parseParameterValue('   ')).toBe('');
  });
});
