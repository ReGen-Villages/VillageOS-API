import { describe, expect, it } from 'vitest';
import { AXIS_BAND, FALLBACK_WIDTH, frameOf, LEFT_GUTTER, PLOT_HEIGHT, RIGHT_GUTTER, TOP_GUTTER } from './chartFrame';

describe('a chart frame', () => {
  it('draws at the fallback width until the card has been measured', () => {
    expect(frameOf(0, [0, 10]).width).toBe(FALLBACK_WIDTH);
    expect(frameOf(600, [0, 10]).width).toBe(600);
  });

  it('places the plot between the gutters, with the axis band under it', () => {
    const frame = frameOf(600, [0, 10]);

    expect(frame.plotTop).toBe(TOP_GUTTER);
    expect(frame.plotBottom).toBe(TOP_GUTTER + PLOT_HEIGHT);
    expect(frame.height).toBe(TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND);
    expect(frame.plotLeft).toBe(LEFT_GUTTER);
    expect(frame.plotRight).toBe(600 - RIGHT_GUTTER);
  });

  it('puts the lowest value at the bottom of the plot and the highest at the top', () => {
    const frame = frameOf(600, [0, 10]);

    expect(frame.y(0)).toBe(frame.plotBottom);
    expect(frame.y(10)).toBe(frame.plotTop);
  });

  it('labels only the round ticks inside the domain', () => {
    expect(frameOf(600, [-1.3, 21.7]).ticks).toEqual([0, 5, 10, 15, 20]);
  });
});
