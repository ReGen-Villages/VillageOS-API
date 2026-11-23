import type { GridValue } from '@tendril-ui/tendril';
import { extentOf } from './gridGeometry';

/** The box a surface is raised in stays between these shares of its width, so a gentle grid still shows its
 *  shape and a steep one stays inside the view. */
const LOWEST_BOX_SHARE = 0.08;
const HIGHEST_BOX_SHARE = 0.4;

export interface RaisedSurface {
  /** x, y, z of each point, row by row from the south, west to east; y is up. */
  positions: Float32Array;
  indices: Uint32Array;
  /** Each point's place on the scale from the lowest value to the highest, nought for an unknown value. */
  shares: Float32Array;
  width: number;
  depth: number;
  boxHeight: number;
  lowest: number;
  highest: number;
}

export function hasTwoRowsAndColumns(grid: GridValue): boolean {
  return grid.rows >= 2 && grid.columns >= 2;
}

export function raisedSurfaceOf(grid: GridValue, exaggeration: number): RaisedSurface | null {
  const { rows, columns, cellSizeMetres, values } = grid;
  if (!hasTwoRowsAndColumns(grid)) return null;
  const range = extentOf(values.filter((value): value is number => value !== null));
  if (!range) return null;

  const width = (columns - 1) * cellSizeMetres;
  const depth = (rows - 1) * cellSizeMetres;
  const across = Math.max(width, depth);
  const spread = range.highest - range.lowest;
  const boxHeight = Math.min(HIGHEST_BOX_SHARE * across, Math.max(LOWEST_BOX_SHARE * across, spread * exaggeration));

  const positions = new Float32Array(rows * columns * 3);
  const shares = new Float32Array(rows * columns);
  values.forEach((value, at) => {
    const row = Math.floor(at / columns);
    const column = at % columns;
    const share = value === null || spread === 0 ? 0 : (value - range.lowest) / spread;
    shares[at] = share;
    positions[at * 3] = column * cellSizeMetres - width / 2;
    positions[at * 3 + 1] = share * boxHeight;
    positions[at * 3 + 2] = depth / 2 - row * cellSizeMetres;
  });

  const triangles: number[] = [];
  const known = (at: number) => values[at] !== null;
  for (let row = 0; row < rows - 1; row++) {
    for (let column = 0; column < columns - 1; column++) {
      const southWest = row * columns + column;
      const southEast = southWest + 1;
      const northWest = southWest + columns;
      const northEast = northWest + 1;
      if (known(southWest) && known(southEast) && known(northWest)) triangles.push(southWest, southEast, northWest);
      if (known(southEast) && known(northEast) && known(northWest)) triangles.push(southEast, northEast, northWest);
    }
  }

  return { positions, indices: new Uint32Array(triangles), shares, width, depth, boxHeight, lowest: range.lowest, highest: range.highest };
}
