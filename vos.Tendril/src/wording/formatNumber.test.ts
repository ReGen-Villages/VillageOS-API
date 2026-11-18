import { describe, it, expect } from 'vitest';
import { formatNumberIn } from './formatNumber';

describe('formatNumberIn', () => {
  it('writes a dash where there is no figure', () => {
    expect(formatNumberIn(null, 'decimal1', 'en')).toBe('—');
    expect(formatNumberIn(undefined, undefined, 'en')).toBe('—');
    expect(formatNumberIn(NaN, 'integer', 'en')).toBe('—');
  });

  it('writes each format a page description can name', () => {
    expect(formatNumberIn(1234.5, undefined, 'en')).toBe('1,234.5');
    expect(formatNumberIn(1234.5, 'integer', 'en')).toBe('1,235');
    expect(formatNumberIn(12.345, 'decimal1', 'en')).toBe('12.3');
    expect(formatNumberIn(12.345, 'decimal2', 'en')).toBe('12.35');
    expect(formatNumberIn(0.813, 'percent', 'en')).toBe('81%');
    expect(formatNumberIn(0.813, 'percent1', 'en')).toBe('81.3%');
    expect(formatNumberIn(81.3, 'percentOutOf100', 'en')).toBe('81%');
    expect(formatNumberIn(2.84, 'hours', 'en')).toBe('2.8 h');
    expect(formatNumberIn(12400, 'compact', 'en')).toBe('12.4K');
    expect(formatNumberIn(1234.5, 'money', 'en')).toBe('$1,234.50');
  });

  it('groups and separates a figure the way the language asked for writes it', () => {
    expect(formatNumberIn(1234.5, 'decimal1', 'de')).toBe('1.234,5');
    // Which no-break space French puts before the sign differs between versions of the locale data.
    expect(formatNumberIn(0.813, 'percent', 'fr')).toMatch(/^81[\u00a0\u202f]%$/);
  });

  it('words two languages in turn without one leaking into the other', () => {
    expect(formatNumberIn(1234.5, 'decimal1', 'en')).toBe('1,234.5');
    expect(formatNumberIn(1234.5, 'decimal1', 'de')).toBe('1.234,5');
    expect(formatNumberIn(1234.5, 'decimal1', 'en')).toBe('1,234.5');
  });

  it('keeps Western digits when the language is Arabic', () => {
    expect(formatNumberIn(1234, 'integer', 'ar-SA')).toMatch(/^[0-9٬,.‎‏\s]+$/);
    expect(formatNumberIn(1234, 'integer', 'ar-SA')).toContain('1');
  });

  it('writes a byte count in binary units and moves up a unit where the rounded figure would read 1,024', () => {
    expect(formatNumberIn(512, 'bytes', 'en')).toBe('512 B');
    expect(formatNumberIn(32_500_000_000, 'bytes', 'en')).toBe('30.3 GiB');
    expect(formatNumberIn(1024 * 1024 - 1, 'bytes', 'en')).toBe('1 MiB');
    expect(formatNumberIn(-2048, 'bytes', 'en')).toBe('-2 KiB');
    expect(formatNumberIn(1024 ** 7, 'bytes', 'en')).toBe('1,048,576 PiB');
    expect(formatNumberIn(1536, 'bytes', 'de')).toBe('1,5 KiB');
  });
});
