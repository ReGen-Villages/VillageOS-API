import { describe, expect, it } from 'vitest';
import { linearScale, niceTicks, paddedDomain } from './chartScale';

describe('a linear scale', () => {
  it('maps the domain onto the range, either way up', () => {
    const y = linearScale([0, 100], [200, 0]);
    expect(y(0)).toBe(200);
    expect(y(100)).toBe(0);
    expect(y(25)).toBe(150);
  });

  it('maps a flat domain to the middle of the range rather than dividing by nothing', () => {
    expect(linearScale([5, 5], [0, 100])(5)).toBe(50);
  });
});

describe('nice ticks', () => {
  it('steps by a round figure and covers the whole domain', () => {
    expect(niceTicks(-8, 37, 5)).toEqual([-10, 0, 10, 20, 30, 40]);
  });

  it('picks a finer step for a narrow domain', () => {
    expect(niceTicks(0, 1, 5)).toEqual([0, 0.2, 0.4, 0.6, 0.8, 1]);
  });

  it('answers the one value twice for a flat domain', () => {
    expect(niceTicks(3, 3, 5)).toEqual([3, 3]);
  });
});

describe('a padded domain', () => {
  it('takes the spec\'s floor and ceiling where it states them', () => {
    expect(paddedDomain([12, 31], -10, 40)).toEqual([-10, 40]);
  });

  it('fits the data with a little air above and below where the spec states none', () => {
    const [floor, ceiling] = paddedDomain([12, 31]);
    expect(floor).toBeLessThan(12);
    expect(ceiling).toBeGreaterThan(31);
    expect(ceiling - floor).toBeLessThan(19 * 1.3);
  });

  it('holds a stated edge and fits the other', () => {
    const [floor, ceiling] = paddedDomain([12, 31], 0);
    expect(floor).toBe(0);
    expect(ceiling).toBeGreaterThan(31);
  });

  it('spans a flat series so a lone value still gets a scale', () => {
    const [floor, ceiling] = paddedDomain([7, 7]);
    expect(floor).toBeLessThan(7);
    expect(ceiling).toBeGreaterThan(7);
  });
});
