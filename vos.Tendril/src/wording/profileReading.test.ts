import { describe, it, expect } from 'vitest';
import { profileFigures, profilePoints, profileSpanOf, profileWords } from './profileReading';

const along = (rows: number, columns: number, values: (number | null)[]) => ({
  originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.0003, longitudeStepDegrees: 0.0003,
  rows, columns, values,
});

describe('the points of a profile', () => {
  it('reads a one-row grid west to east, each point a cell further along', () => {
    expect(profilePoints(along(1, 3, [84, null, 92]))).toEqual([
      { distanceMetres: 0, value: 84 }, { distanceMetres: 30, value: null }, { distanceMetres: 60, value: 92 },
    ]);
  });

  it('reads a one-column grid south to north', () => {
    expect(profilePoints(along(3, 1, [10, 20, 30]))!.map((point) => point.distanceMetres)).toEqual([0, 30, 60]);
  });

  it('reads nothing from a grid of more than one row and column, or from what is not a grid', () => {
    expect(profilePoints(along(2, 2, [1, 2, 3, 4]))).toBeNull();
    expect(profilePoints('a profile')).toBeNull();
  });
});

describe('the figures of a profile', () => {
  const points = profilePoints(along(1, 5, [84, 112, null, 96, 70]))!;

  it('gives the lowest, the highest and their difference over the known values', () => {
    expect(profileFigures(points, null)).toEqual({ lowest: 70, highest: 112, difference: 42 });
  });

  it('gives them within the span alone, its ends included', () => {
    expect(profileFigures(points, { from: 30, to: 90 })).toEqual({ lowest: 96, highest: 112, difference: 16 });
  });

  it('gives nothing where the span holds no known value', () => {
    expect(profileFigures(points, { from: 55, to: 65 })).toBeNull();
  });

  it('takes a span only where both its ends are numbers', () => {
    expect(profileSpanOf(30, 90)).toEqual({ from: 30, to: 90 });
    expect(profileSpanOf(30, null)).toBeNull();
    expect(profileSpanOf(undefined, 90)).toBeNull();
  });

  it('writes them in the language asked, with the unit', () => {
    expect(profileWords({ lowest: 70.25, highest: 1112, difference: 1041.75 }, 'decimal1', 'm', 'de')).toEqual({
      lowest: '70,3 m', highest: '1.112,0 m', difference: '1.041,8 m',
    });
  });
});
