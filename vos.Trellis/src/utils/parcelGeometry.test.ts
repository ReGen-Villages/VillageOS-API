import { describe, it, expect } from 'vitest';
import { borderAreaHectares } from './borderArea';
import { AREA_MATCH_TOLERANCE, areaMatch, draftSquareAround } from './parcelGeometry';

describe('the draft square', () => {
  it.each([0, 45, 60])('placed at latitude %d° measures back the area it was placed from', (latitude) => {
    const square = draftSquareAround({ latitude, longitude: -8.4 }, 24);

    expect(square).toHaveLength(4);
    expect(borderAreaHectares(square, 'meanSphereAreaMethod')).toBeCloseTo(24, 2);
  });

  it('is centred on the point it was placed at', () => {
    const square = draftSquareAround({ latitude: 39.5, longitude: -8.4 }, 24);
    const meanLatitude = square.reduce((sum, corner) => sum + corner.latitude, 0) / square.length;
    const meanLongitude = square.reduce((sum, corner) => sum + corner.longitude, 0) / square.length;

    expect(meanLatitude).toBeCloseTo(39.5, 9);
    expect(meanLongitude).toBeCloseTo(-8.4, 9);
  });

  it('is nothing when the stated area is nothing or less', () => {
    expect(draftSquareAround({ latitude: 39.5, longitude: -8.4 }, 0)).toEqual([]);
    expect(draftSquareAround({ latitude: 39.5, longitude: -8.4 }, -3)).toEqual([]);
  });
});

describe('drawn against stated', () => {
  it('matches when the two are the same', () => {
    expect(areaMatch(24, 24)).toEqual({ kind: 'match' });
  });

  it('still matches just inside the tolerance', () => {
    expect(areaMatch(24 * (1 + AREA_MATCH_TOLERANCE - 0.001), 24)).toEqual({ kind: 'match' });
  });

  it('differs just outside the tolerance, and says by how much', () => {
    const compared = areaMatch(24 * (1 + AREA_MATCH_TOLERANCE + 0.001), 24);

    expect(compared.kind).toBe('differs');
    expect(compared.kind === 'differs' && compared.relativeDifference).toBeCloseTo(
      AREA_MATCH_TOLERANCE + 0.001,
      9,
    );
  });

  it('reports the worked example from the design: 9.7 drawn against 24 stated', () => {
    const compared = areaMatch(9.7, 24);

    expect(compared.kind === 'differs' && compared.relativeDifference).toBeCloseTo(0.5958333, 6);
  });

  it('matches nothing drawn against nothing stated, and nothing else', () => {
    expect(areaMatch(0, 0)).toEqual({ kind: 'match' });
    expect(areaMatch(5, 0)).toEqual({ kind: 'differs', relativeDifference: Infinity });
  });
});
