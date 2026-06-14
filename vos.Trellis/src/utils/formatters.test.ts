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
