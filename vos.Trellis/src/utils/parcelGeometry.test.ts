import { describe, it, expect } from 'vitest';
import { borderAreaHectares } from './borderArea';
import { AREA_MATCH_TOLERANCE, areaMatch, circleOfAreaAround, draftSquareAround, middleOfExtent } from './parcelGeometry';

describe('the middle of a border', () => {
  it('is the middle of the box round it, however its corners are spread', () => {
    const border = [
      { latitude: 25.1, longitude: 55.5 },
      { latitude: 25.1, longitude: 55.6 },
      { latitude: 25.3, longitude: 55.6 },
      { latitude: 25.12, longitude: 55.52 },
    ];

    expect(middleOfExtent(border).latitude).toBeCloseTo(25.2, 9);
    expect(middleOfExtent(border).longitude).toBeCloseTo(55.55, 9);
  });
});

describe('the circle of an approximate area', () => {
  it.each([0, 45, 60])('placed at latitude %d° measures back the area it was placed from', (latitude) => {
    const circle = circleOfAreaAround({ latitude, longitude: -8.4 }, 200);

    expect(Math.abs(borderAreaHectares(circle, 'meanSphereAreaMethod') - 200) / 200).toBeLessThan(0.005);
  });

  it('keeps every corner the same distance from the centre', () => {
    const centre = { latitude: 24, longitude: 47 };
    const circle = circleOfAreaAround(centre, 200);
    const metresPerDegree = (6371008.8 * Math.PI) / 180;
    const distances = circle.map((corner) =>
      Math.hypot(
        (corner.latitude - centre.latitude) * metresPerDegree,
        (corner.longitude - centre.longitude) * metresPerDegree * Math.cos((centre.latitude * Math.PI) / 180),
      ),
    );

    distances.forEach((distance) => expect(distance).toBeCloseTo(Math.sqrt(2_000_000 / Math.PI), 3));
  });

  it('is nothing when the area is nothing or less', () => {
    expect(circleOfAreaAround({ latitude: 24, longitude: 47 }, 0)).toEqual([]);
    expect(circleOfAreaAround({ latitude: 24, longitude: 47 }, -3)).toEqual([]);
  });
});

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
