import { describe, it, expect } from 'vitest';
import { parseParamValue } from './parseParamValue';

// The Params bar (#5647/#5648) parses values as JSON when valid so lists drive fan-out and numbers stay numbers.
describe('parseParamValue', () => {
  it('parses JSON lists, numbers, booleans, and objects', () => {
    expect(parseParamValue('["a","b","c"]')).toEqual(['a', 'b', 'c']);
    expect(parseParamValue('42')).toBe(42);
    expect(parseParamValue('true')).toBe(true);
    expect(parseParamValue('{"k":1}')).toEqual({ k: 1 });
  });

  it('keeps non-JSON text as a plain string', () => {
    expect(parseParamValue('hello')).toBe('hello');
    expect(parseParamValue('  spaced  ')).toBe('  spaced  '); // raw (not the trimmed form) when not JSON
  });

  it('treats empty input as an empty string', () => {
    expect(parseParamValue('')).toBe('');
    expect(parseParamValue('   ')).toBe('');
  });
});
