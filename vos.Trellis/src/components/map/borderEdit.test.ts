import { describe, expect, it } from 'vitest';
import { freehandBoundary, sideMidpoints } from './borderEdit';
import type { BoundaryPoint } from '../../utils/parcelGeometry';

const at = (latitude: number, longitude: number): BoundaryPoint => ({ latitude, longitude });

const triangle = [at(0, 0), at(0, 2), at(2, 2)];

describe('a boundary edited freehand', () => {
  it('takes a placed corner as the last', () => {
    expect(freehandBoundary(triangle, { kind: 'place', corner: at(2, 0) })).toEqual([...triangle, at(2, 0)]);
  });

  it('moves the corner it names and keeps the others', () => {
    expect(freehandBoundary(triangle, { kind: 'move', index: 1, to: at(0, 3) })).toEqual([at(0, 0), at(0, 3), at(2, 2)]);
  });

  it('puts a corner dragged out of a side between that side’s two corners', () => {
    expect(freehandBoundary(triangle, { kind: 'insert', side: 0, corner: at(-1, 1) })).toEqual([
      at(0, 0),
      at(-1, 1),
      at(0, 2),
      at(2, 2),
    ]);
  });

  it('puts a corner dragged out of the closing side after the last corner', () => {
    expect(freehandBoundary(triangle, { kind: 'insert', side: 2, corner: at(1, 0) })).toEqual([...triangle, at(1, 0)]);
  });

  it('leaves the boundary as it is when it is closed, since a freehand boundary is always closed', () => {
    expect(freehandBoundary(triangle, { kind: 'close' })).toBe(triangle);
  });
});

describe('the midpoints of a boundary’s sides', () => {
  it('has one for each side, the closing side last', () => {
    expect(sideMidpoints(triangle)).toEqual([at(0, 1), at(1, 2), at(1, 1)]);
    expect(sideMidpoints([at(0, 0), at(0, 2), at(2, 2), at(2, 0)])).toEqual([at(0, 1), at(1, 2), at(2, 1), at(1, 0)]);
  });

  it('has none for a boundary that encloses nothing', () => {
    expect(sideMidpoints([at(0, 0), at(0, 2)])).toEqual([]);
  });
});
