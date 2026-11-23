import { describe, expect, it } from 'vitest';
import { dropAt, replaceAt } from './listEdits';

describe('replaceAt', () => {
  it('puts the item at the position and leaves the list it was given as it was', () => {
    const held = ['a', 'b', 'c'];
    expect(replaceAt(held, 1, 'x')).toEqual(['a', 'x', 'c']);
    expect(held).toEqual(['a', 'b', 'c']);
  });

  it('changes nothing for a position the list does not have', () => {
    expect(replaceAt(['a'], 3, 'x')).toEqual(['a']);
  });
});

describe('dropAt', () => {
  it('takes the item at the position away and leaves the list it was given as it was', () => {
    const held = ['a', 'b', 'c'];
    expect(dropAt(held, 0)).toEqual(['b', 'c']);
    expect(held).toEqual(['a', 'b', 'c']);
  });
});
