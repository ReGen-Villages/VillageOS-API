import { describe, it, expect } from 'vitest';
import { cellsIn, gridOf } from './gridValue';

const twoByThree = {
  originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.001, longitudeStepDegrees: 0.002,
  rows: 2, columns: 3, values: [1, 2, null, 4, 5, 6],
};

describe('reading a grid', () => {
  it('reads a grid as the platform writes it, and counts its cells', () => {
    expect(gridOf(twoByThree)).toEqual(twoByThree);
    expect(cellsIn(gridOf(twoByThree)!)).toBe(6);
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
