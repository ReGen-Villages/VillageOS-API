import { describe, it, expect } from 'vitest';
import {
  AREA_MATCH_TOLERANCE,
  areaMatch,
  draftSquareAround,
  sphericalAreaHectares,
  type BoundaryPoint,
} from './parcelGeometry';

/** A box one thousandth of a degree on each side, whose south-west corner sits at the given point. */
function box(latitude: number, longitude: number): BoundaryPoint[] {
  const side = 0.001;
  return [
    { latitude, longitude },
    { latitude, longitude: longitude + side },
    { latitude: latitude + side, longitude: longitude + side },
    { latitude: latitude + side, longitude },
  ];
}

describe('the area a boundary encloses', () => {
  // One thousandth of a degree is 111.19 metres along a meridian, and that many metres along a
  // parallel times the cosine of the latitude — which is the whole point: the same box on a flat
  // reading would measure 1.24 hectares wherever it sat.
  it.each([
    [0, 1.23643],
    [45, 0.87428],
    [60, 0.61821],
  ])('at latitude %d° matches the reference figure', (latitude, hectares) => {
    expect(sphericalAreaHectares(box(latitude, -8.4))).toBeCloseTo(hectares, 4);
  });

  it('measures a ring that repeats its first corner the same as one that does not', () => {
    const open = box(45, -8.4);
    const closed = [...open, open[0]];

    expect(sphericalAreaHectares(closed)).toBeCloseTo(sphericalAreaHectares(open), 9);
  });

  it('encloses nothing with fewer than three corners', () => {
    expect(sphericalAreaHectares([])).toBe(0);
    expect(sphericalAreaHectares([{ latitude: 45, longitude: -8.4 }])).toBe(0);
    expect(sphericalAreaHectares(box(45, -8.4).slice(0, 2))).toBe(0);
  });

  it('encloses nothing along a line of collinear corners', () => {
    const line: BoundaryPoint[] = [
      { latitude: 45, longitude: -8.4 },
      { latitude: 45, longitude: -8.3 },
      { latitude: 45, longitude: -8.2 },
    ];

    expect(sphericalAreaHectares(line)).toBeCloseTo(0, 6);
  });
});

describe('the draft square', () => {
  it.each([0, 45, 60])('placed at latitude %d° measures back the area it was placed from', (latitude) => {
    const square = draftSquareAround({ latitude, longitude: -8.4 }, 24);

    expect(square).toHaveLength(4);
    expect(sphericalAreaHectares(square)).toBeCloseTo(24, 2);
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
