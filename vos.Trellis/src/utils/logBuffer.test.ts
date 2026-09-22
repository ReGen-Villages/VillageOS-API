import { describe, it, expect } from 'vitest';
import { appendLines, MAX_LOG_LINES } from './logBuffer';

describe('appendLines', () => {
  it('appends incoming lines to the buffer', () => {
    expect(appendLines(['a', 'b'], ['c', 'd'])).toEqual(['a', 'b', 'c', 'd']);
  });

  it('returns the same array when there is nothing to append', () => {
    const existing = ['a'];
    expect(appendLines(existing, [])).toBe(existing);
  });

  it('does not mutate the existing array', () => {
    const existing = ['a', 'b'];
    appendLines(existing, ['c']);
    expect(existing).toEqual(['a', 'b']);
  });

  it('caps the buffer to the most recent max lines', () => {
    const result = appendLines(['a', 'b', 'c'], ['d', 'e'], 3);
    expect(result).toEqual(['c', 'd', 'e']);
  });

  it('caps when a single append exceeds max', () => {
    const result = appendLines([], ['a', 'b', 'c', 'd'], 2);
    expect(result).toEqual(['c', 'd']);
  });

  it('defaults to MAX_LOG_LINES', () => {
    const many = Array.from({ length: MAX_LOG_LINES + 10 }, (_, i) => `line${i}`);
    const result = appendLines([], many);
    expect(result).toHaveLength(MAX_LOG_LINES);
    expect(result[result.length - 1]).toBe(`line${MAX_LOG_LINES + 9}`);
  });
});
