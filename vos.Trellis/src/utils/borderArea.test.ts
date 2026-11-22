import { describe, expect, it } from 'vitest';
import { borderAreaHectares, type AreaMethod } from './borderArea';
import type { BoundaryPoint } from './parcelGeometry';

// The same figures as vos.Service.Shared's BorderAreaTests, so the page and the intake measure a border alike.
// The WGS84 figures are Karney's geodesic polygon areas on the WGS84 ellipsoid, from geographiclib 2.0.
const referenceBorder: BoundaryPoint[] = [
  [25.1548913568011, 55.55616063595723], [25.15129015552567, 55.54932911385871], [25.14978772652261, 55.5468216600034],
  [25.14886667062588, 55.54735722283466], [25.15562986578318, 55.5691377022649], [25.15707226778431, 55.56848828530388],
  [25.15754361591318, 55.56296846877041], [25.15637327045562, 55.56259408448088], [25.15624859750918, 55.55932941382773],
  [25.1551934630042, 55.55941873916951], [25.15471781966161, 55.55817561038458], [25.15484810477037, 55.55807414688074],
  [25.15462140660673, 55.55752719634192], [25.15409802690795, 55.55771982371159], [25.15378546087098, 55.55684163276631],
].map(([latitude, longitude]) => ({ latitude, longitude }));

const methods: AreaMethod[] = ['meanSphereAreaMethod', 'wgs84EllipsoidAreaMethod'];
const degreeAtEquatorMetres = 111194.9;

function box(latitude: number, side: number): BoundaryPoint[] {
  return [
    { latitude, longitude: 0 },
    { latitude, longitude: side },
    { latitude: latitude + side, longitude: side },
    { latitude: latitude + side, longitude: 0 },
  ];
}

function expectWithin(actual: number, expected: number, tolerance: number) {
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance);
}

describe('borderAreaHectares', () => {
  it('measures the reference border as the platform always has on the mean sphere', () => {
    expectWithin(borderAreaHectares(referenceBorder, 'meanSphereAreaMethod'), 62.484565, 1e-6);
  });

  it('measures the reference border on the WGS84 ellipsoid', () => {
    expectWithin(borderAreaHectares(referenceBorder, 'wgs84EllipsoidAreaMethod'), 62.347761, 62.347761 * 1e-5);
  });

  it.each([
    [0, 0.01, 123.09072],
    [45, 0.01, 87.616345],
    [60, 0.01, 62.158724],
    [-60.01, 0.01, 62.158724],
    [0, 1.0, 1230877.836147],
  ])('measures a box at latitude %s with side %s on the WGS84 ellipsoid as Karney does', (latitude, side, karneyHectares) => {
    expectWithin(borderAreaHectares(box(latitude, side), 'wgs84EllipsoidAreaMethod'), karneyHectares, karneyHectares * 1e-5);
  });

  it('measures a box at the equator as its sides multiplied on the mean sphere', () => {
    const sideMetres = 0.01 * degreeAtEquatorMetres;

    expectWithin(borderAreaHectares(box(0, 0.01), 'meanSphereAreaMethod'), (sideMetres * sideMetres) / 10_000, 0.1);
  });

  it.each(methods)('finds the same box encloses half as much at sixty degrees north (%s)', (method) => {
    const equator = borderAreaHectares(box(0, 0.01), method);

    expectWithin(borderAreaHectares(box(60, 0.01), method), equator * 0.5, equator * 0.01);
  });

  it.each(methods)('measures the same whichever way round the corners were drawn (%s)', (method) => {
    const clockwise = box(39.5, 0.01);

    expectWithin(borderAreaHectares([...clockwise].reverse(), method), borderAreaHectares(clockwise, method), 1e-9);
  });

  it.each(methods)('measures the same for a ring that repeats its first corner (%s)', (method) => {
    const open = box(39.5, 0.01);

    expectWithin(borderAreaHectares([...open, open[0]], method), borderAreaHectares(open, method), 1e-9);
  });

  it.each(methods)('measures a concave border as less than the box around it (%s)', (method) => {
    const notched: BoundaryPoint[] = [
      { latitude: 39.5, longitude: 0 },
      { latitude: 39.5, longitude: 0.01 },
      { latitude: 39.51, longitude: 0.01 },
      { latitude: 39.505, longitude: 0.005 },
      { latitude: 39.51, longitude: 0 },
    ];
    const threeQuarters = borderAreaHectares(box(39.5, 0.01), method) * 0.75;

    expectWithin(borderAreaHectares(notched, method), threeQuarters, threeQuarters * 1e-3);
  });

  it.each(methods)('finds a border of fewer than three corners encloses nothing (%s)', (method) => {
    expect(borderAreaHectares([], method)).toBe(0);
    expect(borderAreaHectares([{ latitude: 39.5, longitude: 0 }, { latitude: 39.5, longitude: 0.01 }], method)).toBe(0);
  });

  it.each(methods)('finds a line of corners in a row encloses nothing (%s)', (method) => {
    const line: BoundaryPoint[] = [
      { latitude: 45, longitude: -8.4 },
      { latitude: 45, longitude: -8.3 },
      { latitude: 45, longitude: -8.2 },
    ];

    expectWithin(borderAreaHectares(line, method), 0, 1e-6);
  });

  // A thousandth of a degree is 111.19 metres along a meridian, and that times the cosine of the latitude along a
  // parallel; a flat reading would give the same box 1.24 hectares wherever it sat.
  it.each([
    [0, 1.23643],
    [45, 0.87428],
    [60, 0.61821],
  ])('measures a thousandth-of-a-degree box at latitude %d° on the mean sphere as the reference figure', (latitude, hectares) => {
    const side = 0.001;
    const small: BoundaryPoint[] = [
      { latitude, longitude: -8.4 },
      { latitude, longitude: -8.4 + side },
      { latitude: latitude + side, longitude: -8.4 + side },
      { latitude: latitude + side, longitude: -8.4 },
    ];

    expect(borderAreaHectares(small, 'meanSphereAreaMethod')).toBeCloseTo(hectares, 4);
  });
});
