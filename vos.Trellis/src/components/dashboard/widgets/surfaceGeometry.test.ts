import { describe, it, expect } from 'vitest';
import { gridOf } from '@tendril-ui/tendril';
import { raisedSurfaceOf } from './surfaceGeometry';

const grid = (rows: number, columns: number, values: (number | null)[], cellSizeMetres = 10) => gridOf({
  originLatitude: 25, originLongitude: 55, cellSizeMetres, latitudeStepDegrees: 0.0001, longitudeStepDegrees: 0.0001,
  rows, columns, values,
})!;

const heightAt = (surface: NonNullable<ReturnType<typeof raisedSurfaceOf>>, point: number) => surface.positions[point * 3 + 1];

describe('a raised surface', () => {
  it('places each point a cell apart across and back, centred, with north away from the viewer', () => {
    const surface = raisedSurfaceOf(grid(2, 3, [0, 0, 0, 0, 0, 0]), 1)!;

    expect(surface.width).toBe(20);
    expect(surface.depth).toBe(10);
    expect([surface.positions[0], surface.positions[2]]).toEqual([-10, 5]);
    expect([surface.positions[5 * 3], surface.positions[5 * 3 + 2]]).toEqual([10, -5]);
  });

  it('raises each point by its value above the lowest, times the exaggeration, when the box stays within its bounds', () => {
    const surface = raisedSurfaceOf(grid(2, 2, [100, 101, 102, 103], 10), 1)!;

    expect(surface.boxHeight).toBeCloseTo(3, 10);
    expect(heightAt(surface, 3)).toBeCloseTo(3, 10);
    expect(raisedSurfaceOf(grid(2, 2, [100, 101, 102, 103], 10), 1.2)!.boxHeight).toBeCloseTo(3.6, 10);
  });

  it('keeps the box at most four tenths of the width, however steep the values', () => {
    const surface = raisedSurfaceOf(grid(2, 2, [0, 0, 0, 1000], 10), 3)!;

    expect(surface.boxHeight).toBeCloseTo(4, 10);
    expect(heightAt(surface, 3)).toBeCloseTo(4, 10);
  });

  it('keeps the box at least eight hundredths of the width, however flat the values', () => {
    expect(raisedSurfaceOf(grid(2, 2, [0, 0, 0, 0.01], 10), 3)!.boxHeight).toBeCloseTo(0.8, 10);
  });

  it('draws no triangle that touches an unknown value', () => {
    expect(raisedSurfaceOf(grid(2, 2, [1, 2, 3, 4]), 1)!.indices).toHaveLength(6);
    expect(raisedSurfaceOf(grid(2, 2, [1, 2, 3, null]), 1)!.indices).toHaveLength(3);
  });

  it('places each point on the scale from the lowest value to the highest', () => {
    const surface = raisedSurfaceOf(grid(2, 2, [10, 20, 30, null]), 1)!;

    expect(Array.from(surface.shares)).toEqual([0, 0.5, 1, 0]);
    expect([surface.lowest, surface.highest]).toEqual([10, 30]);
  });

  it('makes no surface of a grid narrower than two rows and two columns, or with no known value', () => {
    expect(raisedSurfaceOf(grid(1, 3, [1, 2, 3]), 1)).toBeNull();
    expect(raisedSurfaceOf(grid(2, 2, [null, null, null, null]), 1)).toBeNull();
  });
});
