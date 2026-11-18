import { describe, it, expect } from 'vitest';
import { cellBounds, gridOf, mercator, shareOnScale } from './gridGeometry';

const twoByThree = {
  originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.001, longitudeStepDegrees: 0.002,
  rows: 2, columns: 3, values: [1, 2, null, 4, 5, 6],
};

describe('reading a grid', () => {
  it('reads a grid as the platform writes it', () => {
    expect(gridOf(twoByThree)).toEqual(twoByThree);
  });

  it.each([
    ['values that do not fill its rows and columns', { ...twoByThree, values: [1, 2, 3] }],
    ['a value that is neither a number nor unknown', { ...twoByThree, values: [1, 2, 'high', 4, 5, 6] }],
    ['no rows', { ...twoByThree, rows: 0, values: [] }],
    ['a step of nought', { ...twoByThree, latitudeStepDegrees: 0 }],
    ['text', 'a grid'],
    ['nothing', null],
  ])('reads nothing from %s', (_, value) => {
    expect(gridOf(value)).toBeNull();
  });
});

describe('placing a cell', () => {
  it('centres each cell on its point, counting rows from the south and columns from the west', () => {
    expect(cellBounds(gridOf(twoByThree)!, 1, 2)).toEqual({
      south: 25.0005, north: 25.0015, west: 55.003, east: 55.005,
    });
  });
});

describe('projecting to Web Mercator', () => {
  it('puts the meeting of the equator and the prime meridian in the middle', () => {
    expect(mercator(0, 0)).toEqual({ x: 0.5, y: 0.5 });
  });

  it('runs east to the right and north upward', () => {
    const middle = mercator(0, 0);
    const northEast = mercator(10, 10);
    expect(northEast.x).toBeGreaterThan(middle.x);
    expect(northEast.y).toBeLessThan(middle.y);
  });

  it('stretches latitude as Web Mercator does', () => {
    expect(mercator(60, 0).y).toBeCloseTo(0.5 - Math.log(Math.tan(Math.PI / 4 + Math.PI / 6)) / (2 * Math.PI), 10);
  });
});

describe('a value\'s place on the scale', () => {
  it('runs from nought at the floor to one at the ceiling', () => {
    expect(shareOnScale(15, 10, 20, 'linear')).toBe(0.5);
    expect(shareOnScale(5, 10, 20, 'linear')).toBe(0);
    expect(shareOnScale(25, 10, 20, 'linear')).toBe(1);
  });

  it('places a value by its order of magnitude on a logarithmic scale', () => {
    expect(shareOnScale(100, 1, 10_000, 'logarithmic')).toBeCloseTo(0.5, 10);
  });

  it('places nought and below at the floor of a logarithmic scale', () => {
    expect(shareOnScale(0, 1, 10_000, 'logarithmic')).toBe(0);
  });
});
