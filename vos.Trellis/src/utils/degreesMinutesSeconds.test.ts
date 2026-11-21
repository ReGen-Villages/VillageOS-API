import { describe, expect, it } from 'vitest';
import { degreesMinutesSeconds } from './degreesMinutesSeconds';

describe('a position in degrees, minutes and seconds', () => {
  it('names the hemispheres and keeps two decimals of a second', () => {
    expect(degreesMinutesSeconds({ latitude: -26.123456, longitude: 28.654321 }))
      .toBe('26°07′24.44″ S 28°39′15.56″ E');
  });

  it('pads minutes and seconds so a round position still reads as one', () => {
    expect(degreesMinutesSeconds({ latitude: 48.8, longitude: -2.3 }))
      .toBe('48°48′00.00″ N 2°18′00.00″ W');
  });

  it('does not carry a rounded-up second into the minute', () => {
    expect(degreesMinutesSeconds({ latitude: 10.0166666, longitude: 0 }))
      .toBe('10°01′00.00″ N 0°00′00.00″ E');
  });
});
