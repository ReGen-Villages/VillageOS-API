import { describe, expect, it } from 'vitest';
import { sunTimes } from './sunTimes';

describe('when the sun rises and sets', () => {
  // Near Pretoria, in local time: a summer day at the start of January, a winter day near the solstice.
  it('gives a long day in the southern summer and a short one in its winter', () => {
    const summer = sunTimes(1, -25.83, 28.17, 2);
    const winter = sunTimes(172, -25.83, 28.17, 2);
    expect(summer.sunrise).toBeGreaterThan(5);
    expect(summer.sunrise).toBeLessThan(5.5);
    expect(summer.sunset).toBeGreaterThan(19);
    expect(summer.sunset).toBeLessThan(19.5);
    expect(winter.sunrise).toBeGreaterThan(6.7);
    expect(winter.sunset).toBeLessThan(17.6);
  });

  it('answers a day with no night beyond the polar circle in summer, and no day in winter', () => {
    const midsummer = sunTimes(172, 78, 15, 1);
    expect(midsummer.sunset - midsummer.sunrise).toBeCloseTo(24, 5);
    const midwinter = sunTimes(355, 78, 15, 1);
    expect(midwinter.sunset - midwinter.sunrise).toBeCloseTo(0, 5);
  });

  it('shifts with the clock offset the hours are read in', () => {
    const universal = sunTimes(1, -25.83, 28.17, 0);
    const local = sunTimes(1, -25.83, 28.17, 2);
    expect(local.sunrise - universal.sunrise).toBeCloseTo(2, 5);
  });
});
