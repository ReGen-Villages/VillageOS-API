import { describe, expect, it } from 'vitest';
import { rampColour } from './chartRamp';

describe('the sequential ramp', () => {
  it('runs from the light end at nought to the dark end at one', () => {
    expect(rampColour(0, 'light')).toBe('#cde2fb');
    expect(rampColour(1, 'light')).toBe('#0d366b');
  });

  it('interpolates between the steps rather than snapping to one', () => {
    expect(rampColour(0.5, 'light')).not.toBe(rampColour(0.55, 'light'));
    expect(rampColour(0.5, 'light')).toMatch(/^#[0-9a-f]{6}$/);
  });

  it('stops short of the surface on the dark side, so the deep end still reads', () => {
    expect(rampColour(1, 'dark')).toBe('#1c5cab');
  });

  it('clamps a value outside the scale to its ends', () => {
    expect(rampColour(-1, 'light')).toBe('#cde2fb');
    expect(rampColour(2, 'light')).toBe('#0d366b');
  });
});
