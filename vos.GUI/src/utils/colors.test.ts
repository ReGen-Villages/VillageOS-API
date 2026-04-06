import { describe, it, expect } from 'vitest';
import { hashStringToIndex, brightenColor, resolvePredicateColor, INSTANCE_PALETTE, PREDICATE_PALETTE } from './colors';

describe('brightenColor', () => {
  it('returns unchanged color at t=0', () => {
    expect(brightenColor('#ff0000', 0)).toBe('#ff0000');
  });

  it('returns white at t=1', () => {
    expect(brightenColor('#000000', 1)).toBe('#ffffff');
  });

  it('mixes halfway toward white at t=0.5', () => {
    // #000000 mixed 50% toward white → #808080 (128,128,128)
    expect(brightenColor('#000000', 0.5)).toBe('#808080');
  });

  it('brightens a colored hex correctly', () => {
    // #f87171 → r=248, g=113, b=113, t=0.5
    // mix(248) = 248 + (255-248)*0.5 = 251.5 → 252
    // mix(113) = 113 + (255-113)*0.5 = 184
    expect(brightenColor('#f87171', 0.5)).toBe('#fcb8b8');
  });
});

describe('hashStringToIndex', () => {
  it('returns a value within palette bounds', () => {
    const idx = hashStringToIndex('Building', INSTANCE_PALETTE.length);
    expect(idx).toBeGreaterThanOrEqual(0);
    expect(idx).toBeLessThan(INSTANCE_PALETTE.length);
  });

  it('is deterministic — same input always returns same index', () => {
    const a = hashStringToIndex('Sensor', 10);
    const b = hashStringToIndex('Sensor', 10);
    expect(a).toBe(b);
  });

  it('produces different indices for different strings', () => {
    const a = hashStringToIndex('Building', 16);
    const b = hashStringToIndex('Sensor', 16);
    // Not guaranteed but very likely with these strings
    expect(a).not.toBe(b);
  });

  it('handles empty string', () => {
    const idx = hashStringToIndex('', 8);
    expect(idx).toBe(0);
  });

  it('handles single character', () => {
    const idx = hashStringToIndex('A', 8);
    expect(idx).toBeGreaterThanOrEqual(0);
    expect(idx).toBeLessThan(8);
  });

  it('handles palette size of 1', () => {
    const idx = hashStringToIndex('anything', 1);
    expect(idx).toBe(0);
  });

  it('distributes across the palette for similar names', () => {
    const indices = new Set<number>();
    for (let i = 0; i < 20; i++) {
      indices.add(hashStringToIndex(`Type${i}`, PREDICATE_PALETTE.length));
    }
    // With 20 inputs into 8 buckets, should hit at least 4 different buckets
    expect(indices.size).toBeGreaterThanOrEqual(4);
  });
});

describe('resolvePredicateColor', () => {
  it('returns explicit override when available', () => {
    expect(resolvePredicateColor('consumes', { consumes: '#ff0000' })).toBe('#ff0000');
  });

  it('falls back to hash when no override exists', () => {
    const expected = PREDICATE_PALETTE[hashStringToIndex('consumes', PREDICATE_PALETTE.length)];
    expect(resolvePredicateColor('consumes', {})).toBe(expected);
  });

  it('falls back to hash when override map has other keys', () => {
    const expected = PREDICATE_PALETTE[hashStringToIndex('produces', PREDICATE_PALETTE.length)];
    expect(resolvePredicateColor('produces', { consumes: '#ff0000' })).toBe(expected);
  });

  it('is deterministic — same name always returns same color', () => {
    const a = resolvePredicateColor('is', {});
    const b = resolvePredicateColor('is', {});
    expect(a).toBe(b);
  });
});
