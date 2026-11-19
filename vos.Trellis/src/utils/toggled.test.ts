import { describe, expect, it } from 'vitest';
import { toggled } from './toggled';

describe('toggled', () => {
  it('adds an item the set lacks and removes one it holds', () => {
    expect([...toggled(new Set(['a']), 'b')]).toEqual(['a', 'b']);
    expect([...toggled(new Set(['a', 'b']), 'a')]).toEqual(['b']);
  });

  it('leaves the set it was given as it was', () => {
    const held = new Set(['a']);

    const next = toggled(held, 'a');

    expect([...held]).toEqual(['a']);
    expect(next).not.toBe(held);
  });
});
